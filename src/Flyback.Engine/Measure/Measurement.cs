namespace Flyback.Engine.Measure;

/// <summary>What one output socket carried, to the speakers and to the screen.</summary>
/// <param name="Node">The module.</param>
/// <param name="Port">Which of its outputs.</param>
/// <param name="Module">The module's title.</param>
/// <param name="Socket">The output's name.</param>
/// <param name="Sound">One entry for a number, three for a color, as the speakers compute it.</param>
/// <param name="Picture">The same, as the screen computes it.</param>
public sealed record Measurement(
    Guid Node,
    int Port,
    string Module,
    string Socket,
    IReadOnlyList<ComponentStats> Sound,
    IReadOnlyList<ComponentStats> Picture)
{
    /// <summary>
    /// Whether the two halves disagree: one moves and the other does not, the
    /// picture varies across itself, or their ranges differ.
    /// </summary>
    public bool Differs => Sound.Zip(Picture).Any(pair => Disagree(pair.First, pair.Second));

    private static bool Disagree(ComponentStats sound, ComponentStats picture)
    {
        if (picture.Across || sound.OverTime != picture.OverTime) return true;

        // A fiftieth of the range, since the picture is looked at once a frame and
        // misses the sound's last few samples of a ramp.
        var tolerance = Math.Max(1e-6, 0.02 * Math.Max(sound.Max - sound.Min, picture.Max - picture.Min));

        return Math.Abs(sound.Min - picture.Min) > tolerance || Math.Abs(sound.Max - picture.Max) > tolerance;
    }
}
