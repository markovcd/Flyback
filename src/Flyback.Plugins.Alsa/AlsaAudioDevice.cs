using Flyback.Core;
using Flyback.Plugins.Audio;

namespace Flyback.Plugins.Alsa;

/// <summary>
/// Output through libasound's <c>default</c> device. The Linux counterpart of
/// the WASAPI and CoreAudio devices, and the odd one of the three.
/// </summary>
/// <remarks>
/// <para>
/// ALSA has no callback: <c>snd_pcm_writei</c> blocks until the card has room.
/// So this device owns a thread, and that thread is the audio thread — it fills
/// a block from the same callback the other two are handed, and writes it. The
/// contract above is unchanged, which is the point: <see cref="AudioCallback"/>
/// says nothing about who calls it.
/// </para>
/// <para>
/// Every call on the handle is made from one thread, which is what alsa-lib
/// asks for. Stopping therefore asks the writer to finish and waits for it,
/// rather than reaching into a device another thread is inside.
/// </para>
/// </remarks>
public sealed class AlsaAudioDevice(AudioFormat format) : IAudioDevice
{
    /// <summary>
    /// Long enough that a device merely being slow is not mistaken for a device
    /// that has stopped answering: a write returns within one period, and a
    /// period is a few milliseconds.
    /// </summary>
    private const int WriterExitMilliseconds = 2000;

    private readonly int channels = Math.Max(1, format.Channels);

    private IntPtr pcm;
    private Thread? writer;
    private float[] block = [];
    private AudioCallback? fill;
    private volatile bool running;

    /// <summary>
    /// What we asked for. libasound resamples in software when the card cannot
    /// do it, so this stays true of the buffers the engine is asked to fill.
    /// </summary>
    public int SampleRate { get; } = format.SampleRate;

    public bool IsRunning => running;

    public void Start(AudioCallback callback)
    {
        if (pcm != IntPtr.Zero) return;

        Check(
            LibAsound.Open(out var opened, LibAsound.DefaultDevice, LibAsound.PlaybackStream, LibAsound.Blocking),
            $"open the '{LibAsound.DefaultDevice}' device");

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
            LibAsound.Close(opened);
            throw;
        }

        pcm = opened;
        fill = callback;

        // Allocated here, on the caller's thread, so the writer never does. A
        // quarter of the latency is what libasound chose for its own period.
        block = new float[Math.Clamp(SampleRate * format.LatencyMilliseconds / 4000, 64, 4096) * channels];

        running = true;
        writer = new Thread(Write) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} audio" };
        writer.Start();
    }

    public void Stop()
    {
        running = false;

        if (writer is { } thread && !thread.Join(WriterExitMilliseconds))
        {
            // A device that has stopped returning. Leaking the handle is the
            // lesser fault: closing it under a thread that is still writing to
            // it would take the program with it.
            writer = null;
            pcm = IntPtr.Zero;
            fill = null;
            return;
        }

        writer = null;

        if (pcm != IntPtr.Zero)
        {
            LibAsound.Drop(pcm);
            LibAsound.Close(pcm);
            pcm = IntPtr.Zero;
        }

        fill = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The audio thread. Renders a block and writes it, for as long as it is
    /// wanted — and gives up rather than spinning if the device stops taking
    /// samples, since a silent program is better than a hot core.
    /// </summary>
    private void Write()
    {
        try
        {
            while (running && fill is { } deliver)
            {
                deliver(block);

                if (!WriteBlock()) break;
            }
        }
        catch
        {
            // Nothing on this thread can report anything, and an exception
            // leaving it would end the process rather than the sound.
        }

        running = false;
    }

    /// <summary>
    /// Writes one whole block, in as many goes as it takes. False means the
    /// stream is gone and the loop should end.
    /// </summary>
    private unsafe bool WriteBlock()
    {
        fixed (float* start = block)
        {
            var cursor = start;
            var remaining = (nuint)(block.Length / channels);

            while (remaining > 0)
            {
                if (!running) return false;

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
