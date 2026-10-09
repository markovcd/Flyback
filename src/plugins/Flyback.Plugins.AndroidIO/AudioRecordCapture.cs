using Android.Media;
using Flyback.Plugins.Audio;
using AudioFormat = Flyback.Plugins.Audio.AudioFormat;

namespace Flyback.Plugins.AndroidIO;

/// <summary>Interleaved float read from an <see cref="AudioRecord"/> on a thread of its own.</summary>
/// <remarks>
/// The thread first waits for the person to allow the microphone, so <see cref="Start"/> never
/// blocks on the question; a refusal ends the thread, which the host hears as having stopped.
/// Stereo where the microphone has it, mono where it does not.
/// </remarks>
public sealed class AudioRecordCapture(AudioFormat format) : IAudioCapture
{
    private BlockReader? reader;

    public int SampleRate { get; } = format.SampleRate;

    public int Channels { get; private set; } = 2;

    public bool IsRunning => reader?.IsRunning ?? false;

    public void Start(AudioCaptureCallback deliver)
    {
        if (reader is not null) return;

        reader = BlockReader.Start(deliver, Listen);
    }

    public void Stop()
    {
        reader?.Stop();
        reader = null;
    }

    public void Dispose() => Stop();

    /// <summary>Waits for the microphone to be allowed, then opens it; null where it was refused or will not open.</summary>
    private BlockSource? Listen(Func<bool> wanted)
    {
        if (!Microphone.Ask().GetAwaiter().GetResult() || !wanted()) return null;

        var opened = Open(ChannelIn.Stereo) ?? Open(ChannelIn.Mono);
        if (opened is null) return null;

        try
        {
            Channels = opened.ChannelCount;
            opened.StartRecording();
        }
        catch
        {
            opened.Release();
            opened.Dispose();
            throw;
        }

        return new BlockSource(
            new float[Math.Clamp(SampleRate * format.LatencyMilliseconds / 4000, 64, 4096) * Channels],
            block => opened.Read(block, 0, block.Length, 0),
            () =>
            {
                opened.Stop();
                opened.Release();
                opened.Dispose();
            });
    }

    /// <summary>An initialized record, unprocessed where the device allows it, or null where this layout will not open.</summary>
    private AudioRecord? Open(ChannelIn layout)
    {
        var least = AudioRecord.GetMinBufferSize(SampleRate, layout, Encoding.PcmFloat);
        if (least <= 0) return null;

        var channels = layout == ChannelIn.Stereo ? 2 : 1;
        var wanted = SampleRate * format.LatencyMilliseconds / 1000 * channels * sizeof(float);

        foreach (var source in new[] { AudioSource.Unprocessed, AudioSource.Mic })
        {
            AudioRecord? opened = null;

            try
            {
                opened = new AudioRecord.Builder()
                    .SetAudioSource(source)!
                    .SetAudioFormat(new Android.Media.AudioFormat.Builder()
                        .SetEncoding(Encoding.PcmFloat)!
                        .SetSampleRate(SampleRate)!
                        .SetChannelMask((ChannelOut)(int)layout)!
                        .Build()!)!
                    .SetBufferSizeInBytes(Math.Max(least, wanted) * 2)!
                    .Build();

                if (opened?.State == State.Initialized) return opened;
            }
            catch
            {
                // This source, or this layout, is not one the device records from.
            }

            opened?.Release();
        }

        return null;
    }
}
