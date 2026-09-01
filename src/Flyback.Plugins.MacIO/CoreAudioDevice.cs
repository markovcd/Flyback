using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Output through the default output audio unit — the macOS counterpart of the
/// WASAPI device, and the same shape: nothing outside this assembly knows Audio
/// Toolbox exists, and nothing outside it is macOS-only.
/// </summary>
/// <remarks>
/// <para>
/// The default output unit is chosen over talking to a device directly because
/// it is the one that follows the user's choice of output while the program is
/// running, and converts the sample rate when the hardware is not at ours. That
/// makes <see cref="SampleRate"/> the rate we asked for rather than a rate we
/// discovered, which is the honest answer here: it is the rate the callback is
/// rendered at.
/// </para>
/// <para>
/// The render callback is a static function pointer with the device handed to
/// it as context, so no delegate has to be kept alive by hand and no marshalling
/// stub sits on the audio thread. That thread belongs to CoreAudio and has a
/// deadline: the callback allocates nothing, locks nothing, and cannot throw.
/// </para>
/// </remarks>
public sealed unsafe class CoreAudioDevice(AudioFormat format) : IAudioDevice
{
    private readonly int channels = Math.Max(1, format.Channels);

    /// <summary>Keeps this instance findable from the callback's context pointer.</summary>
    private GCHandle self;

    private IntPtr unit;
    private AudioCallback? fill;
    private volatile bool running;

    /// <summary>
    /// What we asked for. The unit resamples if the hardware disagrees, so this
    /// stays true of the buffers the engine is asked to fill.
    /// </summary>
    public int SampleRate { get; } = format.SampleRate;

    public bool IsRunning => running;

    public void Start(AudioCallback callback)
    {
        if (unit != IntPtr.Zero) return;

        fill = callback;
        self = GCHandle.Alloc(this);

        try
        {
            unit = Open();
            Check(AudioToolbox.AudioOutputUnitStart(unit), "start the output unit");
            running = true;
        }
        catch
        {
            // Half an open device is worse than none: leave nothing running and
            // nothing allocated, so a second attempt starts from the same place
            // the first one did.
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (unit != IntPtr.Zero)
        {
            AudioToolbox.AudioOutputUnitStop(unit);

            // Both calls are synchronous with the audio thread — once they have
            // returned the callback is not running and cannot start again, which
            // is what makes it safe to drop the context below.
            AudioToolbox.AudioUnitUninitialize(unit);
            AudioToolbox.AudioComponentInstanceDispose(unit);

            unit = IntPtr.Zero;
        }

        running = false;
        fill = null;

        if (self.IsAllocated) self.Free();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Opens and configures the unit, up to but not including starting it.
    /// Everything here fails loudly: the shell catches it and shows the reason
    /// beside a disabled Audio button.
    /// </summary>
    private IntPtr Open()
    {
        var description = new AudioToolbox.AudioComponentDescription
        {
            ComponentType = AudioToolbox.OutputComponentType,
            ComponentSubType = AudioToolbox.DefaultOutputSubType,
            ComponentManufacturer = AudioToolbox.AppleManufacturer,
        };

        var component = AudioToolbox.AudioComponentFindNext(IntPtr.Zero, description);

        if (component == IntPtr.Zero)
            throw new InvalidOperationException("this machine has no default audio output component.");

        Check(AudioToolbox.AudioComponentInstanceNew(component, out var opened), "open the default output unit");

        try
        {
            Configure(opened);
        }
        catch
        {
            // The instance exists but never became this device's unit, so Stop
            // will not find it. Nobody else can free it either.
            AudioToolbox.AudioComponentInstanceDispose(opened);
            throw;
        }

        return opened;
    }

    /// <summary>
    /// Everything between a unit existing and a unit ready to be started:
    /// the format it is fed in, how much of it at a time, and by whom.
    /// </summary>
    private void Configure(IntPtr opened)
    {
        var stream = new AudioToolbox.AudioStreamBasicDescription
        {
            SampleRate = SampleRate,
            FormatId = AudioToolbox.LinearPcmFormat,
            FormatFlags = AudioToolbox.FloatPacked,
            BytesPerPacket = (uint)(sizeof(float) * channels),
            FramesPerPacket = 1,
            BytesPerFrame = (uint)(sizeof(float) * channels),
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = 8 * sizeof(float),
        };

        // Input scope, because the unit's input is what we are feeding.
        Check(
            AudioToolbox.AudioUnitSetProperty(
                opened,
                AudioToolbox.StreamFormatProperty,
                AudioToolbox.InputScope,
                0,
                stream,
                (uint)sizeof(AudioToolbox.AudioStreamBasicDescription)),
            "set the stream format");

        RequestBufferSize(opened);

        var callback = new AudioToolbox.RenderCallback
        {
            InputProc = (IntPtr)(delegate* unmanaged[Cdecl]<
                IntPtr, uint*, IntPtr, uint, uint, AudioToolbox.AudioBufferList*, int>)&Render,
            InputProcRefCon = GCHandle.ToIntPtr(self),
        };

        Check(
            AudioToolbox.AudioUnitSetProperty(
                opened,
                AudioToolbox.SetRenderCallbackProperty,
                AudioToolbox.InputScope,
                0,
                callback,
                (uint)sizeof(AudioToolbox.RenderCallback)),
            "install the render callback");

        Check(AudioToolbox.AudioUnitInitialize(opened), "initialise the output unit");
    }

    /// <summary>
    /// Asks for a buffer of about the requested latency. Deliberately not
    /// checked: the device may be shared with another program that has already
    /// fixed the size, and playing at someone else's buffer size is much better
    /// than refusing to play.
    /// </summary>
    private void RequestBufferSize(IntPtr opened)
    {
        var frames = (uint)Math.Clamp(SampleRate * format.LatencyMilliseconds / 1000, 64, 4096);

        AudioToolbox.AudioUnitSetProperty(
            opened,
            AudioToolbox.BufferFrameSizeProperty,
            AudioToolbox.GlobalScope,
            0,
            frames,
            sizeof(uint));
    }

    /// <summary>
    /// Called by CoreAudio on its own real-time thread. The buffer is the one
    /// the unit already owns, so filling it in place is what keeps this
    /// allocation-free.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Render(
        IntPtr context,
        uint* actionFlags,
        IntPtr timestamp,
        uint bus,
        uint frames,
        AudioToolbox.AudioBufferList* data)
    {
        try
        {
            if (data is null || data->NumberBuffers == 0) return AudioToolbox.NoError;

            ref var output = ref data->FirstBuffer;

            if (output.Data == IntPtr.Zero) return AudioToolbox.NoError;

            var samples = new Span<float>((void*)output.Data, (int)(output.DataByteSize / sizeof(float)));

            if (GCHandle.FromIntPtr(context).Target is not CoreAudioDevice device || device.fill is not { } deliver)
            {
                samples.Clear();
                return AudioToolbox.NoError;
            }

            // The unit says how many frames it wants and how large the buffer is,
            // and they agree — but a buffer longer than the request would leave a
            // stale tail, and one shorter must not be written past.
            var wanted = Math.Min((int)frames * device.channels, samples.Length);

            // Whole frames only; a frame must never be split across calls.
            wanted -= wanted % device.channels;

            if (wanted < samples.Length) samples[wanted..].Clear();

            deliver(samples[..wanted]);

            return AudioToolbox.NoError;
        }
        catch
        {
            // An exception unwinding into C would take the process with it, and
            // there is nobody on this thread to tell. Silence is the only answer
            // that leaves the program alive.
            if (data is not null && data->NumberBuffers > 0 && data->FirstBuffer.Data != IntPtr.Zero)
                new Span<float>((void*)data->FirstBuffer.Data, (int)(data->FirstBuffer.DataByteSize / sizeof(float)))
                    .Clear();

            return AudioToolbox.NoError;
        }
    }

    private static void Check(int status, string what)
    {
        if (status == AudioToolbox.NoError) return;

        throw new InvalidOperationException($"could not {what}: CoreAudio returned {Describe(status)}.");
    }

    /// <summary>
    /// CoreAudio reports most failures as a four-character code arriving as a
    /// signed integer — <c>'fmt?'</c> is far easier to look up than -10868.
    /// </summary>
    private static string Describe(int status)
    {
        Span<byte> code = [(byte)(status >> 24), (byte)(status >> 16), (byte)(status >> 8), (byte)status];

        foreach (var b in code)
            if (b is < 0x20 or > 0x7E)
                return status.ToString(CultureInfo.InvariantCulture);

        return $"'{Encoding.ASCII.GetString(code)}' ({status.ToString(CultureInfo.InvariantCulture)})";
    }
}
