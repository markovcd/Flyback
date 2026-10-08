using System.Numerics;
using Flyback.Core.Compile;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// Strokes laid out as one path a beam can go round: fitted to -1 to 1, chained
/// nearest first, and spaced evenly by arc length.
/// </summary>
/// <remarks>
/// The spacing is what makes every stroke equally bright, since a beam lays down the
/// same energy each moment. A jump between strokes is one step, the fastest the
/// table can move, so the Beam all but hides it.
/// </remarks>
internal static class ShapeLayout
{
    /// <summary>About how many points one trip round holds; each stroke adds one more.</summary>
    internal const int Points = 4096;

    /// <summary>The most strokes chained, which costs their square to order.</summary>
    internal const int MostStrokes = 10_000;

    /// <param name="strokes">Each a run of points drawn without lifting; a closed one ends where it starts.</param>
    /// <param name="flat">Fit x and y to the square; otherwise fit the whole model to the unit sphere, so it stays in bounds as it turns.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static LoadedShape? Of(IReadOnlyList<Vector3[]> strokes, bool flat, out ShapeFault fault)
    {
        var kept = strokes.Where(stroke => stroke.Length > 0 && stroke.All(Finite)).ToList();

        if (kept.Count == 0)
        {
            fault = ShapeFault.Empty;
            return null;
        }

        if (kept.Count > MostStrokes)
        {
            fault = ShapeFault.TooBig;
            return null;
        }

        Fit(kept, flat);

        var chained = Chain(kept);
        var length = chained.Sum(Length);
        var spacing = length > 0f ? length / Points : 0f;

        var x = new List<float>();
        var y = new List<float>();
        var z = new List<float>();

        foreach (var stroke in chained)
        {
            foreach (var point in Resampled(stroke, spacing))
            {
                x.Add(point.X);
                y.Add(point.Y);
                z.Add(point.Z);
            }
        }

        fault = ShapeFault.None;
        return new LoadedShape([.. x], [.. y], [.. z], chained.Count);
    }

    private static bool Finite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    /// <summary>Centers the strokes and scales them to fill -1 to 1, keeping their proportions.</summary>
    private static void Fit(List<Vector3[]> strokes, bool flat)
    {
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);

        foreach (var point in strokes.SelectMany(stroke => stroke))
        {
            low = Vector3.Min(low, point);
            high = Vector3.Max(high, point);
        }

        var center = (low + high) * 0.5f;
        var half = (high - low) * 0.5f;
        var reach = flat ? MathF.Max(half.X, half.Y) : half.Length();
        var scale = reach > 0f ? 1f / reach : 1f;

        foreach (var stroke in strokes)
        {
            for (var i = 0; i < stroke.Length; i++)
                stroke[i] = (stroke[i] - center) * scale;
        }
    }

    /// <summary>
    /// Orders the strokes so each starts nearest where the last ended, turning one
    /// round when its end is nearer, and entering a closed one at its nearest point.
    /// </summary>
    private static List<Vector3[]> Chain(List<Vector3[]> strokes)
    {
        var left = new List<Vector3[]>(strokes);
        var chained = new List<Vector3[]>(strokes.Count);
        var at = left[0][0];

        while (left.Count > 0)
        {
            var best = 0;
            var reversed = false;
            var nearest = float.MaxValue;

            for (var i = 0; i < left.Count; i++)
            {
                var start = Vector3.DistanceSquared(at, left[i][0]);
                var end = Vector3.DistanceSquared(at, left[i][^1]);

                if (start < nearest)
                {
                    (best, reversed, nearest) = (i, false, start);
                }

                if (end < nearest)
                {
                    (best, reversed, nearest) = (i, true, end);
                }
            }

            var stroke = left[best];
            left[best] = left[^1];
            left.RemoveAt(left.Count - 1);

            if (reversed) Array.Reverse(stroke);
            if (Closed(stroke)) stroke = Entered(stroke, at);

            chained.Add(stroke);
            at = stroke[^1];
        }

        return chained;
    }

    private static bool Closed(Vector3[] stroke) => stroke.Length > 2 && stroke[0] == stroke[^1];

    /// <summary>A closed stroke turned to start and end at its point nearest <paramref name="at"/>.</summary>
    private static Vector3[] Entered(Vector3[] stroke, Vector3 at)
    {
        var loop = stroke.Length - 1;
        var nearest = 0;

        for (var i = 1; i < loop; i++)
        {
            if (Vector3.DistanceSquared(at, stroke[i]) < Vector3.DistanceSquared(at, stroke[nearest])) nearest = i;
        }

        if (nearest == 0) return stroke;

        var turned = new Vector3[stroke.Length];

        for (var i = 0; i < loop; i++) turned[i] = stroke[(nearest + i) % loop];

        turned[^1] = turned[0];
        return turned;
    }

    private static float Length(Vector3[] stroke)
    {
        var length = 0f;

        for (var i = 1; i < stroke.Length; i++) length += Vector3.Distance(stroke[i - 1], stroke[i]);

        return length;
    }

    /// <summary>
    /// A stroke as points <paramref name="spacing"/> apart along it, its two ends
    /// included. A dot is two points in one place, so the beam dwells there.
    /// </summary>
    private static IEnumerable<Vector3> Resampled(Vector3[] stroke, float spacing)
    {
        var length = Length(stroke);
        var steps = spacing > 0f ? Math.Max(1, (int)MathF.Round(length / spacing)) : 1;

        yield return stroke[0];

        var segment = 1;
        var walked = 0f;
        var reach = Vector3.Distance(stroke[0], stroke[Math.Min(1, stroke.Length - 1)]);

        for (var step = 1; step < steps; step++)
        {
            var target = length * step / steps;

            while (segment < stroke.Length - 1 && walked + reach < target)
            {
                walked += reach;
                segment++;
                reach = Vector3.Distance(stroke[segment - 1], stroke[segment]);
            }

            var along = reach > 0f ? Math.Clamp((target - walked) / reach, 0f, 1f) : 0f;

            yield return Vector3.Lerp(stroke[segment - 1], stroke[segment], along);
        }

        yield return stroke[^1];
    }
}
