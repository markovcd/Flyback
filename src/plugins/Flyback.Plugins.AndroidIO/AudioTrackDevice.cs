using Android.Media;
using Flyback.Core;
using Flyback.Plugins.Audio;
using AudioFormat = Flyback.Plugins.Audio.AudioFormat;

namespace Flyback.Plugins.AndroidIO;

/// <summary>Interleaved float written to a streaming <see cref="AudioTrack"/> from a thread of its own.</summary>
/// <remarks>
/// A blocking write returns once the track has room, so the writer is the audio thread, as
/// ALSA's is on Linux.
/// </remarks>
public sealed class AudioTrackDevice(AudioFormat format) : IAudioDevice
{
    /// <summary>Long enough that a slow track is not taken for one that has stopped answering.</summary>
    private const int WriterExitMilliseconds = 2000;

    private readonly int channels = Math.Clamp(format.Channels, 1, 2);

    private AudioTrack? track;
    private Thread? writer;
    private float[] block = [];
    private AudioCallback? fill;
    private volatile bool running;

    public int SampleRate { get; } = format.SampleRate;

    public TimeSpan Latency { get; private set; } = TimeSpan.FromMilliseconds(format.LatencyMilliseconds);

    public bool IsRunning => running;

    public void Start(AudioCallback fill)
    {
        if (track is not null) return;

        var mask = channels == 2 ? ChannelOut.Stereo : ChannelOut.Mono;
        var least = AudioTrack.GetMinBufferSize(SampleRate, mask, Encoding.PcmFloat);

        if (least <= 0)
            throw new InvalidOperationException($"AudioTrack cannot play {SampleRate} Hz float.");

        var wanted = SampleRate * format.LatencyMilliseconds / 1000 * channels * sizeof(float);

        var opened = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)
            .SetAudioFormat(new Android.Media.AudioFormat.Builder()
                .SetEncoding(Encoding.PcmFloat)!
                .SetSampleRate(SampleRate)!
                .SetChannelMask(mask)!
                .Build()!)
            .SetTransferMode(AudioTrackMode.Stream)
            .SetPerformanceMode(AudioTrackPerformanceMode.LowLatency)
            .SetBufferSizeInBytes(Math.Max(least, wanted))
            .Build();

        // Allocated here, so the writer never does: a quarter of the buffer a write.
        var frames = opened.BufferSizeInFrames;
        block = new float[Math.Clamp(frames / 4, 64, 4096) * channels];
        Latency = TimeSpan.FromSeconds((double)frames / SampleRate);

        track = opened;
        this.fill = fill;
        opened.Play();

        running = true;
        writer = new Thread(Write) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} audio" };
        writer.Start();
    }

    public void Stop()
    {
        running = false;

        if (writer is { } thread && !thread.Join(WriterExitMilliseconds))
        {
            // Releasing the track under a thread still writing to it would take the program with it.
            writer = null;
            track = null;
            fill = null;
            return;
        }

        writer = null;

        if (track is { } playing)
        {
            playing.Stop();
            playing.Release();
            playing.Dispose();
            track = null;
        }

        fill = null;
    }

    public void Dispose() => Stop();

    /// <summary>The audio thread: renders a block and writes it, until stopped or the track refuses.</summary>
    private void Write()
    {
        try
        {
            while (running && fill is { } deliver && track is { } playing)
            {
                deliver(block);

                if (playing.Write(block, 0, block.Length, WriteMode.Blocking) < 0) break;
            }
        }
        catch
        {
            // An exception leaving this thread would end the process rather than the sound.
        }

        running = false;
    }
}
