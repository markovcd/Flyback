using Flyback.Core.Compile;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// A drawing as one closed path at constant speed: three tables, x, y and z, read
/// once round per cycle.
/// </summary>
/// <remarks>
/// A table's rate is its point count, so a phase of 0 to 1 read as seconds goes once
/// round. The first point is written again at the end, so the read from the last
/// point back to the first interpolates rather than falling into silence. Strokes
/// are spaced by arc length and a jump between two is a single step, so the beam
/// crosses a jump at once and every stroke equally bright.
/// </remarks>
internal sealed class LoadedShape
{
    /// <param name="x">Across, -1 to 1.</param>
    /// <param name="y">Up, -1 to 1.</param>
    /// <param name="z">Toward the viewer, -1 to 1, and nought for a flat drawing.</param>
    /// <param name="strokes">How many strokes the path chains.</param>
    public LoadedShape(float[] x, float[] y, float[] z, int strokes)
    {
        X = Closed(x);
        Y = Closed(y);
        Z = Closed(z);
        Points = x.Length;
        Strokes = strokes;
    }

    /// <summary>Across.</summary>
    public LoadedSample X { get; }

    /// <summary>Up.</summary>
    public LoadedSample Y { get; }

    /// <summary>Toward the viewer.</summary>
    public LoadedSample Z { get; }

    /// <summary>How many points one trip round holds.</summary>
    public int Points { get; }

    /// <summary>How many strokes one trip round draws.</summary>
    public int Strokes { get; }

    private static LoadedSample Closed(float[] values)
    {
        if (values.Length == 0) return new LoadedSample([0f], 1);

        var closed = new float[values.Length + 1];
        values.CopyTo(closed, 0);
        closed[^1] = values[0];

        return new LoadedSample(closed, values.Length);
    }
}
