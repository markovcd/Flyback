namespace Flyback.App.Audio;

/// <summary>Buffers of sound timed since the engine started, and how many of them fell behind.</summary>
/// <param name="Timed">Buffers rendered by the compiled program and timed.</param>
/// <param name="Late">Of those, the ones that took longer to render than they play for.</param>
public readonly record struct SoundTiming(long Timed, long Late);
