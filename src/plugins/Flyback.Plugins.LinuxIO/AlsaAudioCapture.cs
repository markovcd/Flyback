using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Input through libasound, from its <c>default</c> device or one chosen by name. The
/// mirror of <see cref="AlsaAudioDevice"/>, with the same thread-owns-the-handle shape.
/// </summary>
/// <remarks>
/// Always asks for stereo, whatever the microphone is: libasound's plug layer fills both
/// sides from a mono one, so the host never has to know.
/// </remarks>
/// <param name="format">What to hear: the rate the sound plays at, and how much latency to allow.</param>
/// <param name="device">The PCM name to open, or null for <c>default</c>.</param>
public sealed class AlsaAudioCapture(AudioFormat format, string? device = null) : IAudioCapture
{
    /// <summary>
    /// Every device that could listen, as the name ALSA opens it by and a description a
    /// person can read. Empty where the list cannot be read.
    /// </summary>
    public static IReadOnlyList<SettingOption> Inputs()
    {
        try
        {
            return LibAsound.CaptureDevices()
                .Select(found => new SettingOption(found.Name, found.Description))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private const int Stereo = 2;

    private BlockReader? reader;

    public int SampleRate { get; } = format.SampleRate;

    public int Channels => Stereo;

    public bool IsRunning => reader?.IsRunning ?? false;

    public void Start(AudioCaptureCallback deliver)
    {
        if (reader is not null) return;

        // A chosen device that will not open — unplugged, or taken exclusively by another
        // program — listens to the default instead, as the output does.
        var opened = IntPtr.Zero;

        if (device is null
            || LibAsound.Open(out opened, device, LibAsound.CaptureStream, LibAsound.Blocking) < 0)
        {
            Check(
                LibAsound.Open(out opened, LibAsound.DefaultDevice, LibAsound.CaptureStream, LibAsound.Blocking),
                $"open the '{LibAsound.DefaultDevice}' input");
        }

        try
        {
            Check(
                LibAsound.SetParams(
                    opened,
                    LibAsound.NativeFloatFormat,
                    LibAsound.InterleavedAccess,
                    Stereo,
                    (uint)SampleRate,
                    LibAsound.SoftwareResample,
                    (uint)(format.LatencyMilliseconds * 1000)),
                "configure the input for interleaved float");
        }
        catch
        {
            _ = LibAsound.Close(opened);
            throw;
        }

        // Allocated here so the reader never does.
        var source = new BlockSource(
            new float[Math.Clamp(SampleRate * format.LatencyMilliseconds / 4000, 64, 4096) * Stereo],
            block => ReadBlock(opened, block),
            () =>
            {
                _ = LibAsound.Drop(opened);
                _ = LibAsound.Close(opened);
            });

        reader = BlockReader.Start(deliver, _ => source);
    }

    public void Stop()
    {
        reader?.Stop();
        reader = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Reads one block and returns how many samples arrived, or a negative number once the
    /// device has stopped answering.
    /// </summary>
    private static unsafe int ReadBlock(IntPtr pcm, float[] block)
    {
        fixed (float* start = block)
        {
            var frames = LibAsound.ReadInterleaved(pcm, start, (nuint)(block.Length / Stereo));

            // An overrun or a suspended device: recoverable, and what was missed is gone.
            if (frames < 0) return LibAsound.Recover(pcm, (int)frames, silent: 1) < 0 ? -1 : 0;

            return (int)frames * Stereo;
        }
    }

    private static void Check(int status, string what)
    {
        if (status >= 0) return;

        throw new InvalidOperationException($"could not {what}: {LibAsound.Describe(status)}.");
    }
}
