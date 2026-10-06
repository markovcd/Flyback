namespace Flyback.Plugins.Audio;

/// <summary>
/// A sound input that is open and listening. The mirror of <see cref="IAudioDevice"/>:
/// that is a device being written to, and this is one being read from.
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>The rate the device actually opened at, which may not be the one asked for.</summary>
    int SampleRate { get; }

    /// <summary>How many channels each frame of the buffers holds: one or two.</summary>
    int Channels { get; }

    /// <summary>Whether the device is calling the callback it was started with.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts calling <paramref name="deliver"/> on the capture thread, once for each buffer,
    /// until <see cref="Stop"/> or disposal. Throws if the device cannot be opened.
    /// </summary>
    void Start(AudioCaptureCallback deliver);

    /// <summary>Stops calling the callback it was started with, and lets go of the device.</summary>
    void Stop();
}
