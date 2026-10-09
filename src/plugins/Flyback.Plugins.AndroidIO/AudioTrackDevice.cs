using Android.Media;
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
    private readonly int channels = Math.Clamp(format.Channels, 1, 2);

    private BlockWriter? writer;

    public int SampleRate { get; } = format.SampleRate;

    public TimeSpan Latency { get; private set; } = TimeSpan.FromMilliseconds(format.LatencyMilliseconds);

    public bool IsRunning => writer?.IsRunning ?? false;

    public void Start(AudioCallback fill)
    {
        if (writer is not null) return;

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
        var block = new float[Math.Clamp(frames / 4, 64, 4096) * channels];
        Latency = TimeSpan.FromSeconds((double)frames / SampleRate);

        opened.Play();

        writer = BlockWriter.Start(
            block,
            fill,
            (filled, _) => opened.Write(filled, 0, filled.Length, WriteMode.Blocking) >= 0,
            () =>
            {
                opened.Stop();
                opened.Release();
                opened.Dispose();
            });
    }

    public void Stop()
    {
        writer?.Stop();
        writer = null;
    }

    public void Dispose() => Stop();
}
