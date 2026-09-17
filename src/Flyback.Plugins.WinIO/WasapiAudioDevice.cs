using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
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

    /// <summary>
    /// Held by whatever opens or closes the output: the host's thread, and the pool
    /// thread a change of default reopens it on. Never taken by the callback.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>What the output plays, kept so a change of default can reopen it.</summary>
    private AudioCallback? playing;

    /// <summary>Told when Windows moves its default output, while the default is what plays.</summary>
    private DefaultFollower? follower;

    public void Start(AudioCallback fill)
    {
        lock (gate)
        {
            if (activeOutput is not null) return;

            playing = fill;

            // Looked up on every start rather than once, so a device plugged in after
            // launch is found the next time the sound comes on.
            var chosen = Chosen();

            Open(chosen);

            // The default is a choice that moves: Windows can be told to play
            // somewhere else while the sound is on, and following it is what
            // "System default" means. A chosen device that has gone plays the
            // default too, so it follows as well.
            // One left from a default that would not reopen is let go first.
            follower?.Dispose();
            follower = chosen is null ? DefaultFollower.Watch(() => ThreadPool.QueueUserWorkItem(_ => Reopen())) : null;
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            follower?.Dispose();
            follower = null;
            playing = null;

            Close();
        }
    }

    public void Dispose() => Stop();

    /// <param name="device">The endpoint to play through, or null for the default.</param>
    private void Open(MMDevice? device)
    {
        // The default asked for by role, the same role DefaultFollower listens to,
        // so what is followed is exactly what plays.
        if (device is null)
        {
            using var enumerator = new MMDeviceEnumerator();

            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        }

        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, format.LatencyMilliseconds);

        output.Init(new CallbackSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, format.Channels),
            format.Channels,
            playing!));

        output.Play();

        activeOutput = output;
    }

    private void Close()
    {
        activeOutput?.Stop();
        activeOutput?.Dispose();
        activeOutput = null;
    }

    /// <summary>
    /// Moves the sound to whatever is the default now. The callback is the same one,
    /// so the engine carries on from where it was and never knows the device changed.
    /// </summary>
    /// <remarks>
    /// On a pool thread rather than in the notification, which Windows says must not
    /// block — and stopping an output waits for its playback thread. A change that
    /// arrives after the sound was stopped finds nothing playing and does nothing.
    /// </remarks>
    private void Reopen()
    {
        lock (gate)
        {
            if (playing is null || follower is null) return;

            Close();

            try
            {
                Open(null);
            }
            catch
            {
                // A default that will not open — mid-switch, or nothing left to
                // play through — leaves the sound off rather than taking down a
                // pool thread. The next change of default tries again.
                activeOutput = null;
            }
        }
    }

    /// <summary>
    /// Listens for Windows changing its default output. Registered for as long as the
    /// default is what plays, and the enumerator is kept with it, since that is what
    /// the registration belongs to.
    /// </summary>
    private sealed class DefaultFollower : IMMNotificationClient, IDisposable
    {
        private readonly MMDeviceEnumerator enumerator = new();
        private readonly Action changed;

        private DefaultFollower(Action changed) => this.changed = changed;

        /// <summary>A follower, or null where Windows will not say — the sound still plays, it just stays put.</summary>
        public static DefaultFollower? Watch(Action changed)
        {
            var follower = new DefaultFollower(changed);

            try
            {
                follower.enumerator.RegisterEndpointNotificationCallback(follower);
                return follower;
            }
            catch
            {
                follower.enumerator.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Once per change: Windows reports the console, multimedia and communications
        /// roles separately, and the console role is the one a plain output plays through.
        /// </summary>
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Console) changed();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }

        public void OnDeviceAdded(string pwstrDeviceId) { }

        public void OnDeviceRemoved(string deviceId) { }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            try
            {
                enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch
            {
                // Nothing to undo if Windows has already let it go.
            }

            enumerator.Dispose();
        }
    }

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
