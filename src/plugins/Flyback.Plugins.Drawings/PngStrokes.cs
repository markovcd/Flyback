using System.Numerics;
using Flyback.Core.Compile;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// The outlines of a picture: where its brightness crosses a half, traced by marching
/// squares, simplified, and the longest kept.
/// </summary>
/// <remarks>
/// Line art reads, each line drawn round both its edges; a photograph gives a mess of
/// contours, as in every tool of this kind. The picture is ringed with its own border's
/// brightness first, so every outline closes. Specks a few pixels round are dropped.
/// </remarks>
internal static class PngStrokes
{
    /// <summary>The most pixels traced.</summary>
    internal const int MostPixels = 4096 * 4096;

    /// <summary>The shortest outline kept, in pixels round.</summary>
    private const float Speck = 6f;

    /// <summary>How far, in pixels, a simplified outline may stray from the traced one.</summary>
    private const float Tolerance = 0.4f;

    /// <param name="image">The picture, as read.</param>
    /// <param name="mostStrokes">The most outlines kept, longest first.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static List<Vector3[]>? Read(LoadedImage image, int mostStrokes, out ShapeFault fault)
    {
        if ((long)image.Width * image.Height > MostPixels)
        {
            fault = ShapeFault.TooBig;
            return null;
        }

        var field = Field(image, out var width, out var height);
        var loops = Trace(field, width, height)
            .Select(loop => Simplified(loop, Tolerance))
            .Select(loop => (Loop: loop, Length: Length(loop)))
            .Where(traced => traced.Length >= Speck)
            .OrderByDescending(traced => traced.Length)
            .Take(mostStrokes)
            .Select(traced => traced.Loop.Select(p => new Vector3(p.X, -p.Y, 0f)).ToArray())
            .ToList();

        fault = loops.Count == 0 ? ShapeFault.Empty : ShapeFault.None;
        return loops.Count == 0 ? null : loops;
    }

    /// <summary>
    /// Each pixel's brightness, ringed by one pixel of the border's own, so every
    /// crossing of a half closes inside the ring.
    /// </summary>
    private static float[] Field(LoadedImage image, out int width, out int height)
    {
        width = image.Width + 2;
        height = image.Height + 2;

        var field = new float[width * height];
        var border = 0d;
        var counted = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var at = (y * image.Width + x) * 3;
                var light = 0.2126f * image.Pixels[at] + 0.7152f * image.Pixels[at + 1] + 0.0722f * image.Pixels[at + 2];

                field[(y + 1) * width + x + 1] = light;

                if (x == 0 || y == 0 || x == image.Width - 1 || y == image.Height - 1)
                {
                    border += light;
                    counted++;
                }
            }
        }

        // Pushed off the half, so a border exactly at it still rings the picture.
        var ring = counted == 0 ? 0f : (float)(border / counted);
        if (MathF.Abs(ring - 0.5f) < 0.01f) ring = 0.49f;

        for (var x = 0; x < width; x++) field[x] = field[(height - 1) * width + x] = ring;
        for (var y = 0; y < height; y++) field[y * width] = field[y * width + width - 1] = ring;

        return field;
    }

    /// <summary>
    /// Marching squares at a half: a crossing on each cell edge the level passes, joined
    /// cell by cell into closed loops. Points are in pixels from the picture's corner.
    /// </summary>
    private static IEnumerable<List<Vector2>> Trace(float[] field, int width, int height)
    {
        const float level = 0.5f;

        // An edge's crossing is keyed by the edge: 2·cell for the one across the
        // cell's top, 2·cell + 1 for the one down its left.
        var joined = new Dictionary<long, (long A, long B)>();

        void Join(long a, long b)
        {
            Link(a, b);
            Link(b, a);
        }

        void Link(long from, long to) =>
            joined[from] = joined.TryGetValue(from, out var both) ? (both.A, to) : (to, -1);

        for (var y = 0; y < height - 1; y++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                var corner = y * width + x;
                var a = field[corner] > level;
                var b = field[corner + 1] > level;
                var c = field[corner + width + 1] > level;
                var d = field[corner + width] > level;
                var shape = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);

                if (shape is 0 or 15) continue;

                long top = 2L * corner, left = 2L * corner + 1, bottom = 2L * (corner + width), right = 2L * (corner + 1) + 1;

                switch (shape)
                {
                    case 1 or 14: Join(left, top); break;
                    case 2 or 13: Join(top, right); break;
                    case 3 or 12: Join(left, right); break;
                    case 4 or 11: Join(right, bottom); break;
                    case 6 or 9: Join(top, bottom); break;
                    case 7 or 8: Join(left, bottom); break;
                    case 5 or 10:
                    {
                        // A saddle: the cell's middle says which pair of corners joins.
                        var middle = (field[corner] + field[corner + 1] + field[corner + width] + field[corner + width + 1]) * 0.25f > level;

                        if ((shape == 5) == middle)
                        {
                            Join(left, bottom);
                            Join(top, right);
                        }
                        else
                        {
                            Join(left, top);
                            Join(right, bottom);
                        }

                        break;
                    }
                }
            }
        }

        Vector2 Where(long key)
        {
            var corner = (int)(key / 2);
            var (x, y) = (corner % width, corner / width);
            var (other, along) = key % 2 == 0 ? (corner + 1, Vector2.UnitX) : (corner + width, Vector2.UnitY);
            var from = field[corner];
            var to = field[other];
            var t = to == from ? 0.5f : Math.Clamp((level - from) / (to - from), 0f, 1f);

            // Back from the ringed field to the picture's own pixels.
            return new Vector2(x - 1, y - 1) + along * t;
        }

        var seen = new HashSet<long>();

        foreach (var start in joined.Keys)
        {
            if (!seen.Add(start)) continue;

            var loop = new List<Vector2> { Where(start) };
            var previous = -1L;
            var at = start;

            while (true)
            {
                var (one, two) = joined[at];
                var next = one != previous ? one : two;

                if (next < 0 || next == start || !seen.Add(next)) break;

                loop.Add(Where(next));
                (previous, at) = (at, next);
            }

            loop.Add(loop[0]);
            yield return loop;
        }
    }

    private static float Length(List<Vector2> loop)
    {
        var length = 0f;

        for (var i = 1; i < loop.Count; i++) length += Vector2.Distance(loop[i - 1], loop[i]);

        return length;
    }

    /// <summary>Douglas-Peucker: the fewest of a line's points that keep it within <paramref name="tolerance"/>.</summary>
    private static List<Vector2> Simplified(List<Vector2> line, float tolerance)
    {
        if (line.Count < 4) return line;

        var keep = new bool[line.Count];
        keep[0] = keep[^1] = true;

        // A closed loop's ends are one point, so it is split at the point farthest from them first.
        var far = 0;

        for (var i = 1; i < line.Count - 1; i++)
        {
            if (Vector2.DistanceSquared(line[0], line[i]) > Vector2.DistanceSquared(line[0], line[far])) far = i;
        }

        keep[far] = true;

        var spans = new Stack<(int From, int To)>();
        spans.Push((0, far));
        spans.Push((far, line.Count - 1));

        while (spans.Count > 0)
        {
            var (from, to) = spans.Pop();
            var worst = -1;
            var most = tolerance;

            for (var i = from + 1; i < to; i++)
            {
                var off = Off(line[i], line[from], line[to]);

                if (off > most) (worst, most) = (i, off);
            }

            if (worst < 0) continue;

            keep[worst] = true;
            spans.Push((from, worst));
            spans.Push((worst, to));
        }

        return [.. line.Where((_, i) => keep[i])];
    }

    private static float Off(Vector2 point, Vector2 a, Vector2 b)
    {
        var along = b - a;
        var length = along.Length();

        if (length == 0f) return Vector2.Distance(point, a);

        return MathF.Abs(along.X * (point.Y - a.Y) - along.Y * (point.X - a.X)) / length;
    }
}
