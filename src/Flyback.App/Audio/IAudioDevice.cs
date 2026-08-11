namespace Flyback.App.Audio;

/// <summary>
/// Fills an interleaved stereo buffer. Called on the audio thread, so it must
/// not block, allocate or throw.
/// </summary>
/// <remarks>
/// A named delegate rather than <c>Action&lt;Span&lt;float&gt;&gt;</c>, because
/// a ref struct cannot be a generic type argument.
/// </remarks>
public delegate void AudioCallback(Span<float> interleavedStereo);

/// <summary>
/// A sound output. The only platform-specific surface in the program: the engine
/// produces sample buffers with no dependencies at all, and this is the seam
/// where they meet an actual device.
/// </summary>
public interface IAudioDevice : IDisposable
{
    int SampleRate { get; }

    bool IsRunning { get; }

    void Start(AudioCallback fill);

    void Stop();
}
