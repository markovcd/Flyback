namespace Flyback.Plugins.Audio;

/// <summary>
/// Takes an interleaved buffer of what was just heard. Called on the capture
/// thread, so it must not block, allocate or throw.
/// </summary>
/// <remarks>
/// A named delegate for the reason <see cref="AudioCallback"/> is one.
/// </remarks>
public delegate void AudioCaptureCallback(ReadOnlySpan<float> interleaved);
