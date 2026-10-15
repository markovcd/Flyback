using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Input through an Audio Toolbox input queue, from the default input device or one chosen
/// by its UID. The mirror of <see cref="CoreAudioDevice"/>.
/// </summary>
/// <remarks>
/// Always asks for stereo float at the engine's rate: the queue converts whatever the
/// microphone records to that, so the host never resamples or fills in a side. The callback
/// is a static function pointer with this capture as its context, as the output's is.
/// </remarks>
/// <param name="format">What to hear: the rate the sound plays at, and how much latency to allow.</param>
/// <param name="uid">The UID of the device to listen to, or null for the default input.</param>
public sealed unsafe class CoreAudioCapture(AudioFormat format, string? uid = null) : IAudioCapture
{
    private const int Stereo = 2;

    /// <summary>Enough buffers that one is always being filled while another is read.</summary>
    private const int Buffers = 3;

    /// <summary>
    /// Every input that could listen right now, as its UID and the name the Sound settings
    /// show. Empty where the list cannot be read.
    /// </summary>
    public static IReadOnlyList<SettingOption> Inputs()
    {
        try
        {
            return AudioHardware.InputDevices()
                .Select(device => new SettingOption(device.Uid, device.Name))
                .OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private readonly Lock gate = new();

    /// <summary>Keeps this instance findable from the callback's context pointer.</summary>
    private GCHandle self;

    private IntPtr queue;
    private AudioCaptureCallback? deliver;

    /// <summary>Cleared before the queue stops, so a buffer arriving meanwhile is neither heard nor handed back.</summary>
    private volatile bool listening;

    public int SampleRate { get; } = format.SampleRate;

    public int Channels => Stereo;

    public bool IsRunning => listening;

    public void Start(AudioCaptureCallback deliver)
    {
        lock (gate)
        {
            if (queue != IntPtr.Zero) return;

            if (MicrophoneAccess.IsRefused())
                throw new InvalidOperationException(
                    "macOS is not letting Flyback use the microphone; allow it in System Settings, Privacy & Security, Microphone.");

            this.deliver = deliver;
            self = GCHandle.Alloc(this);

            try
            {
                queue = Open();
                listening = true;
                Check(AudioQueue.Start(queue, IntPtr.Zero), "start the input queue");
            }
            catch
            {
                Close();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (gate) Close();
    }

    public void Dispose() => Stop();

    private void Close()
    {
        listening = false;

        if (queue != IntPtr.Zero)
        {
            // Immediate, so no callback is running once these return and the context can go.
            _ = AudioQueue.Stop(queue, immediate: true);
            _ = AudioQueue.Dispose(queue, immediate: true);

            queue = IntPtr.Zero;
        }

        deliver = null;

        if (self.IsAllocated) self.Free();
    }

    /// <summary>Makes the queue, points it at the chosen device and hands it its buffers, short of starting it.</summary>
    private IntPtr Open()
    {
        var stream = new AudioToolbox.AudioStreamBasicDescription
        {
            SampleRate = SampleRate,
            FormatId = AudioToolbox.LinearPcmFormat,
            FormatFlags = AudioToolbox.FloatPacked,
            BytesPerPacket = sizeof(float) * Stereo,
            FramesPerPacket = 1,
            BytesPerFrame = sizeof(float) * Stereo,
            ChannelsPerFrame = Stereo,
            BitsPerChannel = 8 * sizeof(float),
        };

        Check(
            AudioQueue.NewInput(stream, &Heard, GCHandle.ToIntPtr(self), IntPtr.Zero, IntPtr.Zero, 0, out var opened),
            "open the input queue");

        try
        {
            if (Chosen() is { } device)
            {
                var name = CoreFoundation.NewText(device);

                try
                {
                    Check(AudioQueue.SetProperty(opened, AudioQueue.CurrentDeviceProperty, name, (uint)sizeof(IntPtr)), "choose the input device");
                }
                finally
                {
                    CoreFoundation.Release(name);
                }
            }

            var frames = Math.Clamp(SampleRate * format.LatencyMilliseconds / 2000, 64, 4096);

            for (var i = 0; i < Buffers; i++)
            {
                Check(AudioQueue.AllocateBuffer(opened, (uint)(frames * Stereo * sizeof(float)), out var buffer), "allocate an input buffer");
                Check(AudioQueue.EnqueueBuffer(opened, buffer, 0, IntPtr.Zero), "queue an input buffer");
            }
        }
        catch
        {
            _ = AudioQueue.Dispose(opened, immediate: true);
            throw;
        }

        return opened;
    }

    /// <summary>
    /// The chosen UID where that device is connected now, or null for the default input,
    /// which is also what an unplugged or unknown device gets.
    /// </summary>
    private string? Chosen()
    {
        if (uid is null) return null;

        try
        {
            return AudioHardware.InputDevices().Any(device => device.Uid == uid) ? uid : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Called by the queue on its own thread with a buffer it has filled. The buffer is read
    /// in place and handed back, so nothing here allocates.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Heard(IntPtr context, IntPtr queue, AudioQueue.Buffer* buffer, IntPtr startTime, uint packets, IntPtr descriptions)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is not CoreAudioCapture capture || !capture.listening) return;

            if (buffer->AudioData != IntPtr.Zero && capture.deliver is { } deliver)
            {
                var samples = (int)(buffer->AudioDataByteSize / sizeof(float));

                // Whole frames only.
                samples -= samples % Stereo;

                if (samples > 0) deliver(new ReadOnlySpan<float>((void*)buffer->AudioData, samples));
            }

            _ = AudioQueue.EnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
        }
        catch
        {
            // An exception unwinding into C would take the process with it; this buffer is lost instead.
        }
    }

    private static void Check(int status, string what)
    {
        if (status == AudioToolbox.NoError) return;

        throw new InvalidOperationException($"could not {what}: CoreAudio returned {CoreAudioStatus.Describe(status)}.");
    }
}
