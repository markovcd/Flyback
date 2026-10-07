using System.Globalization;
using System.Numerics;
using System.Text;

namespace Flyback.Engine.Render;

/// <summary>
/// The edges of a Wavefront OBJ model, each drawn once, walked into as few strokes as
/// the mesh allows.
/// </summary>
/// <remarks>
/// A wireframe: an edge two faces share is one edge, and edges behind the model show.
/// Only <c>v</c>, <c>f</c> and <c>l</c> are read; normals, texture coordinates and
/// materials draw nothing. An index outside the vertices read so far is skipped.
/// </remarks>
internal static class ObjStrokes
{
    /// <summary>The most edges kept, each one a stretch of the path.</summary>
    internal const int MostEdges = 200_000;

    /// <param name="bytes">The file.</param>
    /// <param name="mostPoints">The most vertices read.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static List<Vector3[]>? Read(byte[] bytes, int mostPoints, out ShapeFault fault)
    {
        var vertices = new List<Vector3>();
        var edges = new HashSet<(int, int)>();
        var faces = new List<int>();

        using var lines = new StringReader(Encoding.UTF8.GetString(bytes));

        while (lines.ReadLine() is { } line)
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            switch (words[0])
            {
                case "v" when words.Length >= 4:
                    if (vertices.Count >= mostPoints)
                    {
                        fault = ShapeFault.TooBig;
                        return null;
                    }

                    vertices.Add(new Vector3(Number(words[1]), Number(words[2]), Number(words[3])));
                    break;

                case "f" or "l":
                    faces.Clear();

                    for (var i = 1; i < words.Length; i++)
                    {
                        if (Index(words[i], vertices.Count) is { } index) faces.Add(index);
                    }

                    var loop = words[0] == "f" && faces.Count > 2;

                    for (var i = 1; i < faces.Count + (loop ? 1 : 0); i++)
                    {
                        var (a, b) = (faces[i - 1], faces[i % faces.Count]);
                        if (a == b) continue;

                        edges.Add(a < b ? (a, b) : (b, a));

                        if (edges.Count > MostEdges)
                        {
                            fault = ShapeFault.TooBig;
                            return null;
                        }
                    }

                    break;
            }
        }

        if (vertices.Count == 0)
        {
            fault = ShapeFault.NotShape;
            return null;
        }

        fault = edges.Count == 0 ? ShapeFault.Empty : ShapeFault.None;
        return edges.Count == 0 ? null : Walk(vertices, edges);
    }

    private static float Number(string word) =>
        float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : 0f;

    /// <summary>A face's vertex, counted from one or back from the end, its texture and normal indices dropped.</summary>
    private static int? Index(string word, int count)
    {
        var slash = word.IndexOf('/');
        var text = slash < 0 ? word : word[..slash];

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return null;

        var at = index > 0 ? index - 1 : count + index;

        return at >= 0 && at < count ? at : null;
    }

    /// <summary>
    /// Every edge taken once, in trails that go on as long as an unwalked edge leaves
    /// the vertex they reached. Trails start at a vertex with an odd count of edges
    /// where there is one, since that is where a trail has to end or begin.
    /// </summary>
    private static List<Vector3[]> Walk(List<Vector3> vertices, HashSet<(int A, int B)> edges)
    {
        var around = new Dictionary<int, List<int>>();

        void Join(int from, int to)
        {
            if (!around.TryGetValue(from, out var list)) around[from] = list = [];
            list.Add(to);
        }

        foreach (var (a, b) in edges)
        {
            Join(a, b);
            Join(b, a);
        }

        var walked = new HashSet<(int, int)>();
        var strokes = new List<Vector3[]>();
        var starts = around.Keys.OrderBy(v => around[v].Count % 2 == 1 ? 0 : 1).ThenBy(v => v).ToList();

        foreach (var start in starts)
        {
            while (true)
            {
                var trail = new List<Vector3> { vertices[start] };
                var at = start;

                while (around[at].FirstOrDefault(next => !walked.Contains(Key(at, next)), -1) is var next and >= 0)
                {
                    walked.Add(Key(at, next));
                    trail.Add(vertices[next]);
                    at = next;
                }

                if (trail.Count < 2) break;

                strokes.Add([.. trail]);
            }
        }

        return strokes;

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
    }
}
