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

    bool IsRunning { get; }

    void Start(AudioCallback fill);

    void Stop();
}