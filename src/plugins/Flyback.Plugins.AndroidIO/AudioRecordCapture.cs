using Android.Media;
using Flyback.Core;
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
    private const int ReaderExitMilliseconds = 2000;

    private AudioRecord? record;
    private Thread? reader;
    private AudioCaptureCallback? deliver;
    private volatile bool running;

    public int SampleRate { get; } = format.SampleRate;

    public int Channels { get; private set; } = 2;

    public bool IsRunning => running;

    public void Start(AudioCaptureCallback deliver)
    {
        if (reader is not null) return;

        this.deliver = deliver;
        running = true;
        reader = new Thread(Listen) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} input" };
        reader.Start();
    }

    public void Stop()
    {
        running = false;

        if (reader is { } thread && !thread.Join(ReaderExitMilliseconds))
        {
            // Releasing the record under a thread still reading from it would take the program with it.
            reader = null;
            record = null;
            deliver = null;
            return;
        }

        reader = null;
        deliver = null;
    }

    public void Dispose() => Stop();

    private void Listen()
    {
        try
        {
            if (!Microphone.Ask().GetAwaiter().GetResult() || !running) return;

            using var opened = Open(ChannelIn.Stereo) ?? Open(ChannelIn.Mono);
            if (opened is null) return;

            record = opened;
            Channels = opened.ChannelCount;

            var block = new float[Math.Clamp(SampleRate * format.LatencyMilliseconds / 4000, 64, 4096) * Channels];

            opened.StartRecording();

            while (running && deliver is { } hand)
            {
                var read = opened.Read(block, 0, block.Length, 0);
                if (read < 0) break;

                hand(block.AsSpan(0, read));
            }

            opened.Stop();
            opened.Release();
        }
        catch
        {
            // An exception leaving this thread would end the process rather than the listening.
        }
        finally
        {
            record = null;
            running = false;
        }
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
