namespace Flyback.Plugins.Audio;

/// <summary>
/// Fills an interleaved stereo buffer. Called on the audio thread, so it must
/// not block, allocate or throw.
/// </summary>
/// <remarks>
/// A named delegate rather than <c>Action&lt;Span&lt;float&gt;&gt;</c>, because
/// a ref struct cannot be a generic type argument.
/// </remarks>
public delegate void AudioCallback(Span<float> interleavedStereo);