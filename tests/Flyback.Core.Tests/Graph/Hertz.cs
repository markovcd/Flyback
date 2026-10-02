using Flyback.Core.Graph;

namespace Flyback.Core.Tests.Graph;

/// <summary>The closed form of a note's pitch, for comparing the ops the Note module emits to.</summary>
internal static class Hertz
{
    public static float Of(float note) =>
        Pitch.ConcertPitch * MathF.Pow(2f, (note - Pitch.ConcertNote) / Pitch.Semitones);
}
