using Flyback.Core;
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

    private const int ReaderExitMilliseconds = 2000;
    private const int Stereo = 2;

    private IntPtr pcm;
    private Thread? reader;
    private float[] block = [];
    private AudioCaptureCallback? deliver;
    private volatile bool running;

    public int SampleRate { get; } = format.SampleRate;

    public int Channels => Stereo;

    public bool IsRunning => running;

    public void Start(AudioCaptureCallback deliver)
    {
        if (pcm != IntPtr.Zero) return;

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

        pcm = opened;
        this.deliver = deliver;

        // Allocated here so the reader never does.
        block = new float[Math.Clamp(SampleRate * format.LatencyMilliseconds / 4000, 64, 4096) * Stereo];

        running = true;
        reader = new Thread(Read) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} input" };
        reader.Start();
    }

    public void Stop()
    {
        running = false;

        if (reader is { } thread && !thread.Join(ReaderExitMilliseconds))
        {
            // A device that has stopped returning. Leaking the handle is the lesser fault:
            // closing it under a thread still reading from it would take the program with it.
            reader = null;
            pcm = IntPtr.Zero;
            deliver = null;
            return;
        }

        reader = null;

        if (pcm != IntPtr.Zero)
        {
            _ = LibAsound.Drop(pcm);
            _ = LibAsound.Close(pcm);
            pcm = IntPtr.Zero;
        }

        deliver = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The capture thread: reads a block and hands it on for as long as it is wanted, and
    /// gives up rather than spinning if the device stops answering.
    /// </summary>
    private unsafe void Read()
    {
        try
        {
            fixed (float* start = block)
            {
                while (running && deliver is { } hand)
                {
                    var frames = LibAsound.ReadInterleaved(pcm, start, (nuint)(block.Length / Stereo));

                    if (frames < 0)
                    {
                        // An overrun or a suspended device: recoverable, and what was missed is gone.
                        if (LibAsound.Recover(pcm, (int)frames, silent: 1) < 0) break;
                        continue;
                    }

                    hand(block.AsSpan(0, (int)frames * Stereo));
                }
            }
        }
        catch
        {
            // Nothing on this thread can report anything, and an exception leaving it
            // would end the process rather than the sound.
        }

        running = false;
    }

    private static void Check(int status, string what)
    {
        if (status >= 0) return;

        throw new InvalidOperationException($"could not {what}: {LibAsound.Describe(status)}.");
    }
}
