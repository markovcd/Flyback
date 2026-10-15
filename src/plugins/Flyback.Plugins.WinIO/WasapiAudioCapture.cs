using System.Runtime.Versioning;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// WASAPI shared-mode input via NAudio, from the default recording device or one chosen
/// by its endpoint id. The mirror of <see cref="WasapiAudioDevice"/>.
/// </summary>
/// <remarks>
/// Always asks for stereo float at the engine's rate: Windows converts whatever the
/// microphone records to that, so the host never resamples or fills in a side.
/// </remarks>
/// <param name="format">What to hear: the rate the sound plays at, and how much latency to allow.</param>
/// <param name="endpoint">The capture endpoint's id, or null for the default recording device.</param>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioCapture(AudioFormat format, string? endpoint = null) : IAudioCapture
{
    private const int Stereo = 2;

    /// <summary>The shortest buffer asked of Windows, which grants about 10 ms whatever is asked below it.</summary>
    private const int ShortestBufferMilliseconds = 10;

    /// <summary>
    /// Every input that could listen right now, as the id Windows files it under and the
    /// name it shows in its own sound settings. Empty when the list cannot be read.
    /// </summary>
    public static IReadOnlyList<SettingOption> Endpoints()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => new SettingOption(device.ID, device.FriendlyName))
                .OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private readonly Lock gate = new();

    private WasapiRecorder? recorder;

    /// <summary>Cleared by <see cref="Stop"/> or by Windows ending the stream, so a late packet goes nowhere.</summary>
    private volatile bool listening;

    public int SampleRate { get; } = format.SampleRate;

    public int Channels => Stereo;

    public bool IsRunning => listening;

    public void Start(AudioCaptureCallback deliver)
    {
        lock (gate)
        {
            if (recorder is not null) return;

            var buffer = Math.Max(format.LatencyMilliseconds, ShortestBufferMilliseconds);

            // Twice the buffer asked for, so a silent packet is nearly always one slice.
            var silence = new float[Math.Max(SampleRate * buffer / 1000, 64) * Stereo * 2];

            var opened = new WasapiRecorderBuilder()
                .WithDevice(Chosen())
                .WithSharedMode()
                .WithEventSync()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Stereo))
                .WithBufferLength(buffer)
                .Build();

            opened.DataAvailable += (bytes, flags, _, _) =>
            {
                if (listening) WasapiPacket.Deliver(bytes, flags.HasFlag(AudioClientBufferFlags.Silent), silence, deliver);
            };

            // An unplugged device or a stream Windows takes away ends here, which the host
            // hears as having stopped.
            opened.RecordingStopped += (_, _) => listening = false;

            listening = true;

            try
            {
                opened.StartRecording();
            }
            catch (Exception ex)
            {
                listening = false;
                opened.Dispose();

                throw new InvalidOperationException(Explain(ex), ex);
            }

            recorder = opened;
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            listening = false;

            var closing = recorder;
            recorder = null;

            if (closing is null) return;

            try
            {
                closing.StopRecording();
            }
            finally
            {
                closing.Dispose();
            }
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The endpoint that was asked for, or the default recording device where it is unplugged,
    /// disabled or no longer known, as the output does.
    /// </summary>
    private MMDevice Chosen()
    {
        using var enumerator = new MMDeviceEnumerator();

        if (endpoint is not null)
        {
            try
            {
                var device = enumerator.GetDevice(endpoint);

                if (device.State == DeviceState.Active) return device;
            }
            catch
            {
                // Gone, so the default listens.
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
    }

    /// <summary>
    /// Says what to change where Windows' privacy settings are what refused, which its own
    /// message, "access is denied", does not.
    /// </summary>
    private static string Explain(Exception ex) =>
        ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005)
            ? "Windows is not letting desktop apps use the microphone; allow it in Settings, Privacy & security, Microphone."
            : ex.Message;
}
