namespace Flyback.Core.Graph;

/// <summary>
/// One note in a sequence: what it plays, how long it lasts and how loud it is.
/// </summary>
/// <param name="Value">A note number on a Note Sequencer, an ordinary signal on a Sequencer.</param>
/// <param name="Length">In steps, and never zero — a note of no duration has nowhere to sound.</param>
/// <param name="Volume">
/// 0 to 1, and a level rather than a switch, so a rest and a quiet note are the
/// same control.
/// </param>
public readonly record struct Step(float Value, float Length = 1f, float Volume = 1f)
{
    /// <summary>The shortest a note may be, so that a length is always something to divide by.</summary>
    internal const float ShortestLength = 0.01f;

    /// <summary>The same note with every field held to what the sequencer can play.</summary>
    internal Step Sane() => new(
        float.IsFinite(Value) ? Value : 0f,
        float.IsFinite(Length) ? MathF.Max(Length, ShortestLength) : 1f,
        float.IsFinite(Volume) ? Math.Clamp(Volume, 0f, 1f) : 1f);
}