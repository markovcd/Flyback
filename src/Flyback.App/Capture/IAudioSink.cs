namespace Flyback.App.Capture;

/// <summary>
/// Somebody who wants the samples the speakers are getting. The engine knows
/// this and nothing else about recording.
/// </summary>
/// <remarks>
/// <see cref="WriteAudio"/> is called from the sound callback, so an
/// implementation may copy and must not allocate, lock, or wait on anything. The
/// span is only valid for the call.
/// </remarks>
internal interface IAudioSink
{
    /// <summary>One callback's worth, interleaved, exactly as it is about to be heard.</summary>
    void WriteAudio(ReadOnlySpan<float> interleaved);
}
