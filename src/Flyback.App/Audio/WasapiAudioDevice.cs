using NAudio.Wave;

namespace Flyback.App.Audio;

/// <summary>
/// WASAPI shared-mode output via NAudio. This is the one Windows-only class in
/// the program; everything else, including all sample generation, is portable.
/// Replacing it is what would restore Linux and macOS.
/// </summary>
public sealed class WasapiAudioDevice(int sampleRate = 48_000, int latencyMilliseconds = 30) : IAudioDevice
{
    private WasapiOut? _output;

    public int SampleRate { get; } = sampleRate;

    public bool IsRunning => _output?.PlaybackState == PlaybackState.Playing;

    public void Start(AudioCallback fill)
    {
        if (_output is not null) return;

        var output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, latencyMilliseconds);
        output.Init(new CallbackSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2), fill));
        output.Play();

        _output = output;
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Hands NAudio's pull-model read straight to the callback. Slicing the
    /// array NAudio already owns keeps this allocation-free.
    /// </summary>
    private sealed class CallbackSampleProvider(WaveFormat format, AudioCallback fill) : ISampleProvider
    {
        public WaveFormat WaveFormat => format;

        public int Read(float[] buffer, int offset, int count)
        {
            // Whole frames only; a stereo frame must never be split across calls.
            count -= count % 2;
            fill(buffer.AsSpan(offset, count));
            return count;
        }
    }
}
