using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// Output through an ASIO driver, on its first two output channels.
/// </summary>
/// <remarks>
/// The driver calls us, once per half of its double buffer, on a thread of its own. Nothing is
/// held until <see cref="Start"/>; <see cref="Open"/> asks the driver its rate first, so the engine
/// renders at whichever the driver will play. A driver that has since moved to a rate it cannot
/// leave refuses to start rather than play at another pitch. The block size is the driver's own,
/// set in its control panel.
/// </remarks>
/// <param name="format">What to play; its rate is set on the driver.</param>
/// <param name="load">Creates the driver, on the thread it will live on.</param>
internal sealed unsafe class AsioAudioDevice(AudioFormat format, Func<AsioDriver> load) : IAudioDevice
{
    /// <summary>The device the driver's callbacks reach, which carry no pointer of their own.</summary>
    private static AsioAudioDevice? playing;

    /// <summary>Held by whatever starts or stops the driver, never by its callbacks.</summary>
    private readonly Lock gate = new();

    private AsioThread? thread;
    private AsioDriver? driver;
    private Asio.Callbacks* callbacks;
    private IntPtr[] halves = [];
    private int[] types = [];
    private float[] block = [];
    private int frames;
    private bool tellReady;
    private AudioCallback? fill;
    private volatile bool running;

    public int SampleRate => format.SampleRate;

    /// <summary>What the driver says its output latency is, or one block where it says nothing.</summary>
    public TimeSpan Latency { get; private set; }

    public bool IsRunning => running;

    /// <summary>A device at the rate asked for where the driver can be set to it, and at the driver's own where it cannot.</summary>
    public static AsioAudioDevice Open(AudioFormat format, Func<AsioDriver> load) =>
        new(format with { SampleRate = RateFor(format.SampleRate, load) }, load);

    /// <summary>
    /// Loads the driver for long enough to ask its rate. A device already playing answers instead,
    /// since a driver loaded twice may stop the one playing.
    /// </summary>
    private static int RateFor(int wanted, Func<AsioDriver> load)
    {
        if (Volatile.Read(ref playing) is { } other) return other.SampleRate;

        var rate = wanted;

        try
        {
            using var thread = new AsioThread();

            thread.Invoke(() =>
            {
                using var driver = load();

                if (!driver.Init(IntPtr.Zero)) return;

                var own = driver.SampleRate();

                if (own > 0 && own != wanted && !driver.CanSampleRate(wanted)) rate = own;
            });
        }
        catch
        {
            // Start loads the driver again and says what is wrong with it.
        }

        return rate;
    }

    public void Start(AudioCallback fill)
    {
        lock (gate)
        {
            if (running) return;

            // A driver that asked to be reset, or stopped some other way, is let go first.
            Teardown();

            this.fill = fill;
            thread = new AsioThread();

            try
            {
                thread.Invoke(Play);
            }
            catch
            {
                Teardown();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (gate) Teardown();
    }

    public void Dispose() => Stop();

    /// <summary>Loads the driver and starts it. On the driver's thread.</summary>
    private void Play()
    {
        if (Interlocked.CompareExchange(ref playing, this, null) is { } other && other != this)
            throw new InvalidOperationException("another ASIO driver is already playing.");

        driver = load();

        if (!driver.Init(IntPtr.Zero))
            throw new InvalidOperationException(Said(driver.ErrorMessage, "the driver would not start"));

        var own = driver.SampleRate();

        if (own != SampleRate && (!driver.CanSampleRate(SampleRate) || driver.SetSampleRate(SampleRate) != Asio.Ok))
            throw new InvalidOperationException(own > 0
                ? $"the driver is at {own} Hz and cannot be set to {SampleRate} Hz; restart Flyback to play at {own} Hz."
                : $"the driver cannot play at {SampleRate} Hz.");

        var (_, outputs) = driver.Channels();

        if (outputs < 1) throw new InvalidOperationException("the driver has no outputs.");

        frames = driver.PreferredBufferSize();

        if (frames <= 0) throw new InvalidOperationException("the driver gave no block size.");

        var channels = Math.Min(2, outputs);

        types = new int[channels];

        for (var channel = 0; channel < channels; channel++)
        {
            types[channel] = driver.OutputType(channel);

            if (Asio.Width(types[channel]) == 0)
                throw new InvalidOperationException($"the driver plays samples of type {types[channel]}, which Flyback cannot write.");
        }

        callbacks = (Asio.Callbacks*)NativeMemory.AllocZeroed((nuint)sizeof(Asio.Callbacks));
        callbacks->BufferSwitch = &BufferSwitch;
        callbacks->SampleRateDidChange = &SampleRateDidChange;
        callbacks->Message = &Message;
        callbacks->BufferSwitchTimeInfo = &BufferSwitchTimeInfo;

        Span<Asio.BufferInfo> infos = stackalloc Asio.BufferInfo[channels];

        for (var channel = 0; channel < channels; channel++) infos[channel] = new Asio.BufferInfo { IsInput = 0, Channel = channel };

        if (driver.CreateBuffers(infos, frames, callbacks) != Asio.Ok)
            throw new InvalidOperationException(Said(driver.ErrorMessage, "the driver would not make its buffers"));

        halves = new IntPtr[2 * channels];

        for (var channel = 0; channel < channels; channel++)
        {
            halves[channel] = infos[channel].First;
            halves[channels + channel] = infos[channel].Second;
        }

        block = new float[frames * 2];
        tellReady = driver.OutputReady() == Asio.Ok;

        var (_, latency) = driver.Latencies();
        Latency = TimeSpan.FromSeconds((latency > 0 ? latency : frames) / (double)SampleRate);

        running = true;

        if (driver.Start() != Asio.Ok)
            throw new InvalidOperationException(Said(driver.ErrorMessage, "the driver would not play"));
    }

    /// <summary>Stops and releases the driver, then its thread. Under <see cref="gate"/>.</summary>
    private void Teardown()
    {
        running = false;

        if (thread is null) return;

        thread.Invoke(Close);
        thread.Dispose();
        thread = null;
    }

    /// <summary>On the driver's thread. A driver that has stopped calls back no more.</summary>
    private void Close()
    {
        if (driver is not null)
        {
            _ = driver.Stop();
            _ = driver.DisposeBuffers();
            driver.Dispose();
            driver = null;
        }

        if (callbacks is not null)
        {
            NativeMemory.Free(callbacks);
            callbacks = null;
        }

        halves = [];
        fill = null;

        Interlocked.CompareExchange(ref playing, null, this);
    }

    /// <summary>
    /// Plays the same callback through a freshly loaded driver, as a driver asks after its buffer size or
    /// rate was changed in its control panel. On a pool thread, since the driver asked from its own.
    /// </summary>
    private void Reset()
    {
        lock (gate)
        {
            if (!running || fill is not { } again) return;

            Teardown();

            try
            {
                Start(again);
            }
            catch
            {
                // A driver that will not come back leaves the sound off, as at launch.
            }
        }
    }

    /// <summary>Renders one block into the half of the double buffer the driver is about to play.</summary>
    private void Cycle(int half)
    {
        var chunk = block.AsSpan(0, frames * 2);

        try
        {
            if (running && fill is { } deliver) deliver(chunk);
            else chunk.Clear();
        }
        catch
        {
            // Nothing on this thread can report anything; the block is silence.
            chunk.Clear();
        }

        var channels = types.Length;

        for (var channel = 0; channel < channels; channel++)
            Write(halves[half * channels + channel], types[channel], chunk, channel);

        if (tellReady) _ = driver?.OutputReady();
    }

    /// <summary>Writes one channel of an interleaved stereo block into a driver buffer, in the driver's sample type.</summary>
    internal static void Write(IntPtr buffer, int type, ReadOnlySpan<float> stereo, int channel)
    {
        var count = stereo.Length / 2;

        for (var i = 0; i < count; i++)
        {
            var sample = Math.Clamp(stereo[2 * i + channel], -1f, 1f);

            switch (type)
            {
                case Asio.Float32:
                    ((float*)buffer)[i] = sample;
                    break;
                case Asio.Float64:
                    ((double*)buffer)[i] = sample;
                    break;
                case Asio.Int16:
                    ((short*)buffer)[i] = (short)Math.Round(sample * short.MaxValue);
                    break;
                case Asio.Int24:
                    var packed = (int)Math.Round(sample * 8_388_607.0);
                    var bytes = (byte*)buffer + 3 * i;
                    bytes[0] = (byte)packed;
                    bytes[1] = (byte)(packed >> 8);
                    bytes[2] = (byte)(packed >> 16);
                    break;
                default:
                    ((int*)buffer)[i] = (int)Math.Round(sample * Ceiling(type));
                    break;
            }
        }
    }

    /// <summary>The largest value a 32-bit container of <paramref name="type"/> holds.</summary>
    private static double Ceiling(int type) => type switch
    {
        Asio.Int32In16 => 32_767.0,
        Asio.Int32In18 => 131_071.0,
        Asio.Int32In20 => 524_287.0,
        Asio.Int32In24 => 8_388_607.0,
        _ => int.MaxValue,
    };

    private static string Said(string message, string otherwise) => message.Length > 0 ? message : otherwise + ".";

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void BufferSwitch(int half, int direct)
    {
        try
        {
            Volatile.Read(ref playing)?.Cycle(half & 1);
        }
        catch
        {
            // The device is going.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr BufferSwitchTimeInfo(IntPtr time, int half, int direct)
    {
        try
        {
            Volatile.Read(ref playing)?.Cycle(half & 1);
        }
        catch
        {
            // The device is going.
        }

        return time;
    }

    /// <summary>The rate is set at start and checked there; a change made in the driver's panel asks for a reset too.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SampleRateDidChange(double rate)
    {
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Message(int selector, int value, IntPtr message, double* opt)
    {
        switch (selector)
        {
            case Asio.SelectorSupported:
                return value is Asio.EngineVersion or Asio.ResetRequest or Asio.ResyncRequest or Asio.LatenciesChanged ? 1 : 0;
            case Asio.EngineVersion:
                return 2;
            case Asio.ResetRequest:
                if (Volatile.Read(ref playing) is { } device) ThreadPool.QueueUserWorkItem(_ => device.Reset());
                return 1;
            case Asio.ResyncRequest:
            case Asio.LatenciesChanged:
                return 1;
            default:
                return 0;
        }
    }
}
