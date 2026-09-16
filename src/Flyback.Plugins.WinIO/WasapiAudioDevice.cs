using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// WASAPI shared-mode output via NAudio. Nothing outside this assembly knows
/// NAudio exists, and nothing outside it is Windows-only.
/// </summary>
/// <param name="format">What to play.</param>
/// <param name="endpoint">
/// The render endpoint's id, or null for whatever Windows is playing through.
/// </param>
public sealed class WasapiAudioDevice(AudioFormat format, string? endpoint = null) : IAudioDevice
{
    private WasapiOut? activeOutput;

    public int SampleRate { get; } = format.SampleRate;

    public bool IsRunning => activeOutput?.PlaybackState == PlaybackState.Playing;

    /// <summary>
    /// Every output that could play right now, as the id Windows files it under and
    /// the name it shows in its own sound settings.
    /// </summary>
    /// <remarks>
    /// Here rather than beside the form, because this is the file that may touch
    /// NAudio. Empty when the list cannot be read — the audio service stopped, say —
    /// since a form is not somewhere to fail from, and the default is still offered.
    /// </remarks>
    public static IReadOnlyList<SettingOption> Endpoints()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => new SettingOption(device.ID, device.FriendlyName))
                .OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Start(AudioCallback fill)
    {
        if (activeOutput is not null) return;

        // Looked up on every start rather than once, so a device plugged in after
        // launch is found the next time the sound comes on.
        var output = Chosen() is { } device
            ? new WasapiOut(device, AudioClientShareMode.Shared, true, format.LatencyMilliseconds)
            : new WasapiOut(AudioClientShareMode.Shared, format.LatencyMilliseconds);

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
    /// The endpoint that was asked for, or null for the default — which is also what
    /// a device that is unplugged, disabled or no longer known gets, because sound
    /// through the wrong speakers is easier to notice and fix than no sound at all.
    /// </summary>
    private MMDevice? Chosen()
    {
        if (endpoint is null) return null;

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            var device = enumerator.GetDevice(endpoint);

            return device.State == DeviceState.Active ? device : null;
        }
        catch
        {
            return null;
        }
    }

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
