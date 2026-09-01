using Flyback.Plugins.Audio;
using NAudio.Wave;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// WASAPI shared-mode output via NAudio. Nothing outside this assembly knows
/// NAudio exists, and nothing outside it is Windows-only.
/// </summary>
public sealed class WasapiAudioDevice(AudioFormat format) : IAudioDevice
{
    private WasapiOut? activeOutput;

    public int SampleRate { get; } = format.SampleRate;

    public bool IsRunning => activeOutput?.PlaybackState == PlaybackState.Playing;

    public void Start(AudioCallback fill)
    {
        if (activeOutput is not null) return;

        var output = new WasapiOut(
            NAudio.CoreAudioApi.AudioClientShareMode.Shared,
            format.LatencyMilliseconds);

        output.Init(new CallbackSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, format.Channels),
            format.Channels,
            fill));

        output.Play();

        activeOutput = output;
    }

    public void Stop()
    {
        activeOutput?.Stop();
        activeOutput?.Dispose();
        activeOutput = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Hands NAudio's pull-model read straight to the callback. Slicing the
    /// array NAudio already owns keeps this allocation-free.
    /// </summary>
    private sealed class CallbackSampleProvider(WaveFormat format, int channels, AudioCallback fill) : ISampleProvider
    {
        public WaveFormat WaveFormat => format;

        public int Read(float[] buffer, int offset, int count)
        {
            // Whole frames only; a frame must never be split across calls.
            count -= count % channels;
            fill(buffer.AsSpan(offset, count));
            return count;
        }
    }
}
