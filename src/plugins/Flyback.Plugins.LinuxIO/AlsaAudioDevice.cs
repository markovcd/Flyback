using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// Output through libasound, to its <c>default</c> device or one chosen by name. The
/// Linux counterpart of the WASAPI and CoreAudio devices, and the odd one of the three.
/// </summary>
/// <remarks>
/// ALSA has no callback: <c>snd_pcm_writei</c> blocks until the card has room, so this
/// device owns a thread and that thread is the audio thread. The contract above is
/// unchanged, since <see cref="AudioCallback"/> says nothing about who calls it. Every
/// call on the handle after it is opened is made from that one thread, which is what
/// alsa-lib asks for, so the writer closes it too.
/// </remarks>
/// <param name="format">What to play.</param>
/// <param name="device">
/// The PCM name to open, or null for <c>default</c> — which on a desktop is PipeWire
/// or PulseAudio, and they move the stream when the system's default output changes,
/// so nothing here has to listen for it.
/// </param>
public sealed class AlsaAudioDevice(AudioFormat format, string? device = null) : IAudioDevice
{
    /// <summary>
    /// Every device that could play, as the name ALSA opens it by and a description a
    /// person can read. Empty where the list cannot be read, since a form is not
    /// somewhere to fail from, and the default is still offered.
    /// </summary>
    public static IReadOnlyList<SettingOption> Outputs()
    {
        try
        {
            return LibAsound.PlaybackDevices()
                .Select(found => new SettingOption(found.Name, found.Description))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private readonly int channels = Math.Max(1, format.Channels);

    private BlockWriter? writer;

    /// <summary>
    /// What we asked for. libasound resamples in software when the card cannot
    /// do it, so this stays true of the buffers the engine is asked to fill.
    /// </summary>
    public int SampleRate { get; } = format.SampleRate;

    /// <summary>The latency it asks libasound for.</summary>
    public TimeSpan Latency => TimeSpan.FromMilliseconds(format.LatencyMilliseconds);

    public bool IsRunning => writer?.IsRunning ?? false;

    public void Start(AudioCallback fill)
    {
        if (writer is not null) return;

        // A chosen device that will not open — unplugged, or taken exclusively by
        // another program — plays the default instead, because sound through the
        // wrong speakers is easier to notice and fix than no sound at all.
        var opened = IntPtr.Zero;

        if (device is null
            || LibAsound.Open(out opened, device, LibAsound.PlaybackStream, LibAsound.Blocking) < 0)
        {
            Check(
                LibAsound.Open(out opened, LibAsound.DefaultDevice, LibAsound.PlaybackStream, LibAsound.Blocking),
                $"open the '{LibAsound.DefaultDevice}' device");
        }

        try
        {
            Check(
                LibAsound.SetParams(
                    opened,
                    LibAsound.NativeFloatFormat,
                    LibAsound.InterleavedAccess,
                    (uint)channels,
                    (uint)SampleRate,
                    LibAsound.SoftwareResample,
                    (uint)(format.LatencyMilliseconds * 1000)),
                "configure the device for interleaved float");
        }
        catch
        {
            // Open succeeded, so this handle is ours to close and nothing else
            // knows about it yet.
            _ = LibAsound.Close(opened);
            throw;
        }

        // Allocated here, on the caller's thread, so the writer never does. A
        // quarter of the latency is what libasound chose for its own period.
        var block = new float[Math.Clamp(SampleRate * format.LatencyMilliseconds / 4000, 64, 4096) * channels];

        writer = BlockWriter.Start(
            block,
            fill,
            (filled, wanted) => WriteBlock(opened, filled, wanted),
            () =>
            {
                _ = LibAsound.Drop(opened);
                _ = LibAsound.Close(opened);
            });
    }

    public void Stop()
    {
        writer?.Stop();
        writer = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Writes one whole block, in as many goes as it takes. False means the
    /// stream is gone and the writer should end.
    /// </summary>
    private unsafe bool WriteBlock(IntPtr pcm, float[] block, Func<bool> wanted)
    {
        fixed (float* start = block)
        {
            var cursor = start;
            var remaining = (nuint)(block.Length / channels);

            while (remaining > 0)
            {
                if (!wanted()) return false;

                var written = LibAsound.WriteInterleaved(pcm, cursor, remaining);

                if (written < 0)
                {
                    // An underrun or a suspended device: recoverable, and the
                    // rest of this block is dropped rather than retried, because
                    // by the time the stream is back the next block is fresher.
                    return LibAsound.Recover(pcm, (int)written, silent: 1) >= 0;
                }

                cursor += written * channels;
                remaining -= (nuint)written;
            }
        }

        return true;
    }

    private static void Check(int status, string what)
    {
        if (status >= 0) return;

        throw new InvalidOperationException($"could not {what}: {LibAsound.Describe(status)}.");
    }
}
