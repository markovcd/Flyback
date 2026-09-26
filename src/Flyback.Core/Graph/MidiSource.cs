namespace Flyback.Core.Graph;

/// <summary>
/// Something that can play the synth from outside the patch: a keyboard on a USB
/// cable, or the one under your hands right now.
/// </summary>
/// <param name="Id">
/// Stable, and what a saved patch stores. A port number would not do — plug the
/// same keyboard into the other socket and the patch would point at nothing.
/// </param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct MidiSource(string Id, string Name)
{
    /// <summary>
    /// Whether it sends a clock, which is what a fresh Clock In looks for. Said by
    /// the shell, which knows the instrument; the engine only reads it.
    /// </summary>
    public bool Conducts { get; init; }
}