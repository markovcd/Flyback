namespace Flyback.Plugins.Audio;

/// <summary>
/// A sound output. The whole platform-specific surface of the program: the
/// engine produces sample buffers with no dependencies at all, and this is the
/// seam where they meet an actual device.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>The rate the device actually opened at, which may not be the one asked for.</summary>
    int SampleRate { get; }

    /// <summary>Whether the device is calling the callback it was started with.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// How long a sample takes from the callback that fills it to the speaker, as
    /// near as the device can tell. Nought where it cannot.
    /// </summary>
    TimeSpan Latency => TimeSpan.Zero;

    /// <summary>
    /// Starts calling <paramref name="fill"/> on the audio thread, once for each buffer, until
    /// <see cref="Stop"/> or disposal.
    /// </summary>
    void Start(AudioCallback fill);

    /// <summary>Stops calling the callback it was started with.</summary>
    void Stop();
}