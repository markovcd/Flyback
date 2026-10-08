using System.Globalization;
using System.Numerics;
using System.Xml;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// The strokes an SVG draws: every path, line, polyline, polygon, rectangle, circle
/// and ellipse, curves flattened, each element's transforms applied.
/// </summary>
/// <remarks>
/// Outlines, not fills: a filled shape is drawn round its edge, which is what a beam
/// can show. Nothing under defs, a clip, a mask, a marker, a pattern or a symbol is
/// drawn, nor anything hidden with display="none". No DTD is read and nothing is
/// fetched, and a malformed file gives what came before the fault.
/// </remarks>
internal static class SvgStrokes
{
    /// <summary>How many lines a Bézier curve is flattened into.</summary>
    private const int CurveSteps = 16;

    /// <summary>How many lines a whole turn of an arc is flattened into.</summary>
    private const int TurnSteps = 48;

    private static readonly HashSet<string> Unseen =
        ["defs", "clipPath", "mask", "marker", "pattern", "symbol", "metadata", "title", "desc", "style", "script", "text"];

    /// <param name="bytes">The file.</param>
    /// <param name="mostPoints">Stops reading past this many points, and says so.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static List<Vector3[]>? Read(byte[] bytes, int mostPoints, out ShapeFault fault)
    {
        var strokes = new List<Vector3[]>();
        var points = 0;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0,
        };

        var sawSvg = false;

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            var transforms = new Stack<Matrix3x2>();
            transforms.Push(Matrix3x2.Identity);

            var read = true;

            // Skip leaves the reader on the node after the one it skipped, so that one is not read past.
            while (read ? reader.Read() : !reader.EOF)
            {
                read = true;

                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (transforms.Count > 1) transforms.Pop();
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element) continue;

                sawSvg |= reader.LocalName == "svg";

                if (Unseen.Contains(reader.LocalName) || reader.GetAttribute("display") == "none")
                {
                    if (!reader.IsEmptyElement)
                    {
                        reader.Skip();
                        read = false;
                    }

                    continue;
                }

                var matrix = Transform(reader.GetAttribute("transform")) * transforms.Peek();

                foreach (var stroke in Element(reader, matrix))
                {
                    points += stroke.Count;

                    if (points > mostPoints)
                    {
                        fault = ShapeFault.TooBig;
                        return null;
                    }

                    if (stroke.Count > 0) strokes.Add([.. stroke.Select(p => new Vector3(p.X, -p.Y, 0f))]);
                }

                if (!reader.IsEmptyElement) transforms.Push(matrix);
            }
        }
        catch (XmlException)
        {
            // A broken file gives what was read before the break.
        }

        fault = !sawSvg ? ShapeFault.NotShape : strokes.Count == 0 ? ShapeFault.Empty : ShapeFault.None;
        return fault == ShapeFault.None ? strokes : null;
    }

    private static IEnumerable<List<Vector2>> Element(XmlReader reader, Matrix3x2 matrix)
    {
        float At(string name) => Number(reader.GetAttribute(name));

        var drawn = reader.LocalName switch
        {
            "path" => PathData(reader.GetAttribute("d") ?? string.Empty),
            "line" => [[new(At("x1"), At("y1")), new(At("x2"), At("y2"))]],
            "polyline" => [Pairs(reader.GetAttribute("points"))],
            "polygon" => [Closed(Pairs(reader.GetAttribute("points")))],
            "rect" => [Rectangle(At("x"), At("y"), At("width"), At("height"))],
            "circle" => [Ellipse(At("cx"), At("cy"), At("r"), At("r"))],
            "ellipse" => [Ellipse(At("cx"), At("cy"), At("rx"), At("ry"))],
            _ => new List<List<Vector2>>(),
        };

        return drawn.Select(stroke => stroke.Select(p => Vector2.Transform(p, matrix)).ToList());
    }

    private static List<Vector2> Closed(List<Vector2> points)
    {
        if (points.Count > 1 && points[0] != points[^1]) points.Add(points[0]);
        return points;
    }

    private static List<Vector2> Rectangle(float x, float y, float width, float height) =>
        width <= 0f || height <= 0f
            ? []
            : [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height), new(x, y)];

    private static List<Vector2> Ellipse(float cx, float cy, float rx, float ry)
    {
        if (rx <= 0f || ry <= 0f) return [];

        var points = new List<Vector2>(TurnSteps + 1);

        for (var i = 0; i <= TurnSteps; i++)
        {
            var angle = MathF.Tau * i / TurnSteps;
            points.Add(new(cx + rx * MathF.Cos(angle), cy + ry * MathF.Sin(angle)));
        }

        points[^1] = points[0];
        return points;
    }

    private static List<Vector2> Pairs(string? text)
    {
        var numbers = new Tokens(text ?? string.Empty);
        var points = new List<Vector2>();

        while (numbers.Number(out var x) && numbers.Number(out var y)) points.Add(new(x, y));

        return points;
    }

    /// <summary>A leading number, units ignored, and nought where there is none.</summary>
    private static float Number(string? text) =>
        new Tokens(text ?? string.Empty).Number(out var value) ? value : 0f;

    /// <summary>The matrix a transform list says, applied right to left as SVG reads it.</summary>
    internal static Matrix3x2 Transform(string? text)
    {
        var matrix = Matrix3x2.Identity;
        if (string.IsNullOrWhiteSpace(text)) return matrix;

        var at = 0;

        while (at < text.Length)
        {
            var open = text.IndexOf('(', at);
            var close = open < 0 ? -1 : text.IndexOf(')', open);
            if (close < 0) break;

            var name = text[at..open].Trim(' ', ',', '\t', '\r', '\n');
            var tokens = new Tokens(text[(open + 1)..close]);
            var args = new List<float>();

            while (args.Count < 6 && tokens.Number(out var value)) args.Add(value);

            float Arg(int i, float otherwise = 0f) => i < args.Count ? args[i] : otherwise;

            var one = name switch
            {
                "matrix" when args.Count == 6 => new Matrix3x2(args[0], args[1], args[2], args[3], args[4], args[5]),
                "translate" => Matrix3x2.CreateTranslation(Arg(0), Arg(1)),
                "scale" => Matrix3x2.CreateScale(Arg(0, 1f), Arg(1, Arg(0, 1f))),
                "rotate" => Matrix3x2.CreateRotation(Arg(0) * MathF.PI / 180f, new Vector2(Arg(1), Arg(2))),
                "skewX" => Matrix3x2.CreateSkew(Arg(0) * MathF.PI / 180f, 0f),
                "skewY" => Matrix3x2.CreateSkew(0f, Arg(0) * MathF.PI / 180f),
                _ => Matrix3x2.Identity,
            };

            matrix = one * matrix;
            at = close + 1;
        }

        return matrix;
    }

    /// <summary>The subpaths a path's <c>d</c> draws, each a list of points.</summary>
    internal static List<List<Vector2>> PathData(string data)
    {
        var strokes = new List<List<Vector2>>();
        var tokens = new Tokens(data);
        var current = new List<Vector2>();
        var at = Vector2.Zero;
        var start = Vector2.Zero;
        var control = Vector2.Zero;
        var previous = ' ';

        void Lift()
        {
            if (current.Count > 1) strokes.Add(current);
            current = [];
        }

        void To(Vector2 point)
        {
            if (current.Count == 0) current.Add(at);
            current.Add(point);
            at = point;
        }

        while (tokens.Command(out var command))
        {
            var relative = char.IsLower(command);
            var origin = relative ? at : Vector2.Zero;
            var kind = char.ToUpperInvariant(command);
            var first = true;

            // A command's arguments may repeat without the letter, until the next letter.
            while (first || tokens.StartsNumber())
            {
                origin = relative ? at : Vector2.Zero;

                switch (kind)
                {
                    case 'M':
                        if (!tokens.Point(out var moved)) goto next;
                        if (first)
                        {
                            Lift();
                            at = start = origin + moved;
                        }
                        else To(origin + moved);
                        break;

                    case 'L':
                        if (!tokens.Point(out var line)) goto next;
                        To(origin + line);
                        break;

                    case 'H':
                        if (!tokens.Number(out var across)) goto next;
                        To(new Vector2((relative ? at.X : 0f) + across, at.Y));
                        break;

                    case 'V':
                        if (!tokens.Number(out var up)) goto next;
                        To(new Vector2(at.X, (relative ? at.Y : 0f) + up));
                        break;

                    case 'C':
                    {
                        if (!tokens.Point(out var c1) || !tokens.Point(out var c2) || !tokens.Point(out var end)) goto next;
                        Cubic(at, origin + c1, origin + c2, origin + end);
                        control = origin + c2;
                        break;
                    }

                    case 'S':
                    {
                        if (!tokens.Point(out var c2) || !tokens.Point(out var end)) goto next;
                        var c1 = previous is 'C' or 'S' ? 2f * at - control : at;
                        Cubic(at, c1, origin + c2, origin + end);
                        control = origin + c2;
                        break;
                    }

                    case 'Q':
                    {
                        if (!tokens.Point(out var c) || !tokens.Point(out var end)) goto next;
                        Quadratic(at, origin + c, origin + end);
                        control = origin + c;
                        break;
                    }

                    case 'T':
                    {
                        if (!tokens.Point(out var end)) goto next;
                        var c = previous is 'Q' or 'T' ? 2f * at - control : at;
                        Quadratic(at, c, origin + end);
                        control = c;
                        break;
                    }

                    case 'A':
                    {
                        if (!tokens.Number(out var rx) || !tokens.Number(out var ry) || !tokens.Number(out var turn)
                            || !tokens.Flag(out var large) || !tokens.Flag(out var sweep) || !tokens.Point(out var end))
                        {
                            goto next;
                        }

                        Arc(at, rx, ry, turn, large, sweep, origin + end);
                        break;
                    }

                    case 'Z':
                        if (current.Count > 0 && at != start) To(start);
                        Lift();
                        at = start;
                        previous = 'Z';
                        goto next;

                    default:
                        goto next;
                }

                previous = kind;
                first = false;
            }

            next:
            previous = kind;
        }

        Lift();
        return strokes;

        void Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = i / (float)CurveSteps;
                var u = 1f - t;
                To(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }
        }

        void Quadratic(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = i / (float)CurveSteps;
                var u = 1f - t;
                To(u * u * p0 + 2f * u * t * p1 + t * t * p2);
            }
        }

        // The endpoint form turned to the center form, as the SVG specification's
        // appendix does it, then walked by angle.
        void Arc(Vector2 from, float rx, float ry, float degrees, bool large, bool sweep, Vector2 end)
        {
            rx = MathF.Abs(rx);
            ry = MathF.Abs(ry);

            if (from == end) return;

            if (rx == 0f || ry == 0f)
            {
                To(end);
                return;
            }

            var phi = degrees * MathF.PI / 180f;
            var (sin, cos) = MathF.SinCos(phi);
            var half = (from - end) * 0.5f;
            var x1 = cos * half.X + sin * half.Y;
            var y1 = -sin * half.X + cos * half.Y;

            var grow = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);

            if (grow > 1f)
            {
                rx *= MathF.Sqrt(grow);
                ry *= MathF.Sqrt(grow);
            }

            var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            var root = denominator > 0f ? MathF.Sqrt(MathF.Max(0f, numerator / denominator)) : 0f;
            if (large == sweep) root = -root;

            var cx1 = root * rx * y1 / ry;
            var cy1 = -root * ry * x1 / rx;
            var center = new Vector2(cos * cx1 - sin * cy1, sin * cx1 + cos * cy1) + (from + end) * 0.5f;

            var startAngle = MathF.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx);
            var endAngle = MathF.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
            var delta = endAngle - startAngle;

            if (sweep && delta < 0f) delta += MathF.Tau;
            if (!sweep && delta > 0f) delta -= MathF.Tau;

            var steps = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(delta) / MathF.Tau * TurnSteps));

            for (var i = 1; i < steps; i++)
            {
                var angle = startAngle + delta * i / steps;
                var (s, c) = MathF.SinCos(angle);
                To(center + new Vector2(cos * rx * c - sin * ry * s, sin * rx * c + cos * ry * s));
            }

            To(end);
        }
    }

    /// <summary>Reads path data and point lists: letters, numbers in any of the forms SVG allows, and arc flags.</summary>
    private sealed class Tokens(string text)
    {
        private int at;

        public bool Command(out char command)
        {
            Space();

            while (at < text.Length && !char.IsAsciiLetter(text[at]) && !StartsNumber())
            {
                at++;
                Space();
            }

            if (at < text.Length && char.IsAsciiLetter(text[at]) && text[at] is not ('e' or 'E'))
            {
                command = text[at++];
                return true;
            }

            command = ' ';
            return false;
        }

        public bool StartsNumber()
        {
            Space();
            return at < text.Length && (char.IsAsciiDigit(text[at]) || text[at] is '-' or '+' or '.');
        }

        public bool Point(out Vector2 point)
        {
            point = default;

            if (!Number(out var x) || !Number(out var y)) return false;

            point = new Vector2(x, y);
            return true;
        }

        public bool Flag(out bool flag)
        {
            Space();
            flag = false;

            if (at >= text.Length || text[at] is not ('0' or '1')) return false;

            flag = text[at++] == '1';
            return true;
        }

        public bool Number(out float value)
        {
            value = 0f;
            Space();

            var begin = at;

            if (at < text.Length && text[at] is '-' or '+') at++;

            var digits = false;
            var dot = false;

            while (at < text.Length && (char.IsAsciiDigit(text[at]) || (text[at] == '.' && !dot)))
            {
                dot |= text[at] == '.';
                digits |= char.IsAsciiDigit(text[at]);
                at++;
            }

            if (digits && at < text.Length && text[at] is 'e' or 'E')
            {
                var mark = at;
                at++;

                if (at < text.Length && text[at] is '-' or '+') at++;

                if (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    while (at < text.Length && char.IsAsciiDigit(text[at])) at++;
                }
                else at = mark;
            }

            if (!digits)
            {
                at = begin;
                return false;
            }

            return float.TryParse(text.AsSpan(begin, at - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && float.IsFinite(value);
        }

        private void Space()
        {
            while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == ',')) at++;
        }
    }
}
