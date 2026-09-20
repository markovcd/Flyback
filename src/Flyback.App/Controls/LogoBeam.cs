using System.Globalization;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The mark as a patch rather than a drawing: a beam sweeping the two ramps and
/// trailing behind itself, over the retrace it flies back along.
/// </summary>
/// <remarks>
/// Every coordinate is <c>docs/logo.svg</c>'s 256-unit box mapped onto the frame's
/// -1..1, which is the same mapping <see cref="LogoMark"/> scales by, so the
/// drawing and the patch put the mark in one place and swapping between them
/// does not move it.
/// </remarks>
internal static class LogoBeam
{
    private const double Box = 256;

    /// <summary>The stroke's half-width, which is the retrace bar's radius.</summary>
    private const double Radius = 21 / 2d;

    /// <summary>How long one ramp takes, in seconds. Two of them are a cycle.</summary>
    private const double Ramp = 1.25;

    /// <summary>
    /// The patch. Picture only: it is opened from a dialog, where a sound nobody
    /// asked for is a fault rather than a surprise.
    /// </summary>
    public static Patch Patch()
    {
        var b = new PatchBuilder();

        var (rampFromX, rampFromY) = At(73, 92);
        var (rampToX, rampToY) = At(183, 56);
        var (lowFromX, lowFromY) = At(73, 148);
        var (lowToX, lowToY) = At(165, 118);

        // The retrace runs from where the upper ramp starts, down past the lower one.
        var (_, barBottom) = At(73, 200);
        var inner = N(Radius * 0.92 / (Box / 2));
        var outer = N(Radius * 1.08 / (Box / 2));

        var coord = b.Add(NodeCatalog.CoordTypeId);
        var clock = b.Add(NodeCatalog.TimeTypeId);

        // Where in the cycle the beam is, which half of it that is, and how far
        // along that half's ramp. The last twentieth of each half is the flyback
        // itself, clamped to the ramp's end so the beam rests on the far corner
        // rather than smearing back across what it just drew.
        var phase = Formula(b, $"fract(a / {N(Ramp * 2)})");
        var half = Formula(b, "step(0.5, a)");
        var along = Formula(b, "min(fract(a * 2) / 0.95, 1)");

        var beamX = Formula(b, $"mix(mix({rampFromX}, {rampToX}, a), mix({lowFromX}, {lowToX}, a), b)");
        var beamY = Formula(b, $"mix(mix({rampFromY}, {rampToY}, a), mix({lowFromY}, {lowToY}, a), b)");

        var awayX = b.Add("math.sub");
        var awayY = b.Add("math.sub");
        var distance = b.Add("math.hypot");

        // Edges the wrong way round, so a Smoothstep reads 1 inside the beam and
        // 0 outside it. The core is the same dot again, tighter, added on top to
        // push the middle of the beam past full and so towards white.
        var glow = b.Add("math.smoothstep", (0, 0.098f), (1, 0.055f));
        var core = b.Add("math.smoothstep", (0, 0.042f), (1, 0.012f));
        var lit = Formula(b, "a + b * 1.2");

        // The sweep's three accents are one arc of hue, so the gradient the mark
        // is drawn with is a hue falling as the beam crosses.
        var hue = Formula(b, "0.57 - a * 0.16");
        var beam = b.Add("color.hsv", (1, 0.66f));

        // A round-capped bar: the distance to the segment the retrace runs along,
        // dimmed, since it sits behind the ramps rather than among them.
        var bar = Formula(
            b,
            $"0.5 * (1 - smoothstep({inner}, {outer}, "
            + $"hypot(a - ({rampFromX}), b - clamp(b, {barBottom}, {rampFromY}))))");

        var retrace = b.Add(
            "color.ink",
            (2, Colors.Sink.R / 255f),
            (3, Colors.Sink.G / 255f),
            (4, Colors.Sink.B / 255f));

        // Everything is drawn before the Trails rather than over it: what it
        // reads back is the frame, so a color laid on afterwards is added to its
        // own echo every frame until it is white.
        var trail = b.Add("feedback.trails", (7, 0.985f));

        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, phase, 0)
         .Wire(phase, 0, half, 0)
         .Wire(phase, 0, along, 0)
         .Wire(along, 0, beamX, 0)
         .Wire(half, 0, beamX, 1)
         .Wire(along, 0, beamY, 0)
         .Wire(half, 0, beamY, 1)

         .Wire(coord, NodeCatalog.CoordXPort, awayX, 0)
         .Wire(beamX, 0, awayX, 1)
         .Wire(coord, NodeCatalog.CoordYPort, awayY, 0)
         .Wire(beamY, 0, awayY, 1)
         .Wire(awayX, 0, distance, 0)
         .Wire(awayY, 0, distance, 1)
         .Wire(distance, 0, glow, 2)
         .Wire(distance, 0, core, 2)
         .Wire(glow, 0, lit, 0)
         .Wire(core, 0, lit, 1)

         .Wire(along, 0, hue, 0)
         .Wire(hue, 0, beam, 0)
         .Wire(lit, 0, beam, 2)

         .Wire(beam, 0, retrace, 0)
         .Wire(coord, NodeCatalog.CoordXPort, bar, 0)
         .Wire(coord, NodeCatalog.CoordYPort, bar, 1)
         .Wire(bar, 0, retrace, 1)

         .Wire(retrace, 0, trail, 0)
         .Wire(trail, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>An Expression carrying <paramref name="formula"/>.</summary>
    private static NodeInstance Formula(PatchBuilder b, string formula)
    {
        var node = b.Add(NodeCatalog.ExpressionTypeId);

        node.SetState("expression", new JsonObject { ["formula"] = formula });

        return node;
    }

    /// <summary>A point of the logo's box as the frame reads it, y flipped.</summary>
    private static (string X, string Y) At(double x, double y) =>
        (N(x / (Box / 2) - 1), N(1 - y / (Box / 2)));

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
