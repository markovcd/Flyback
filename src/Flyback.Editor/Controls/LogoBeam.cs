using System.Globalization;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The mark as a patch rather than a drawing: a beam sweeping the two ramps and
/// trailing behind itself, over the retrace it flies back along, on the face of a
/// tube that scans, bows and drifts out of sync.
/// </summary>
/// <remarks>
/// Every coordinate is <c>docs/logo.svg</c>'s 256-unit box mapped onto the frame's
/// -1..1, which is the same mapping <see cref="LogoMark"/> scales by, so the
/// drawing and the patch put the mark in one place at the middle of the face and
/// part towards the corners, by as much as the glass bows.
/// <para>
/// The tube is light and nothing else: every one of its faults multiplies what is
/// emitted, and the surface underneath is laid on afterwards untouched. So a scan
/// line darkens a glow rather than cutting a stripe out of the window, and the
/// glass is the dialog's own color wherever the beam has not been. Past the rim
/// is the dark of the set around it.
/// </para>
/// </remarks>
internal static class LogoBeam
{
    private const double Box = 256;

    /// <summary>The stroke's half-width, which is the retrace bar's radius.</summary>
    private const double Radius = 21 / 2d;

    /// <summary>How long one ramp takes, in seconds. Two of them are a cycle.</summary>
    private const double Ramp = 1.25;

    /// <summary>How far the glass pushes the picture out at the corners.</summary>
    private const double Bulge = 0.22;

    /// <summary>
    /// How many bright lines the face is scanned in, across the frame's two units.
    /// </summary>
    /// <remarks>
    /// Few, because the frames are drawn at twice the size they are shown and a
    /// line thinner than a couple of pixels averages back into a flat gray on the
    /// way down.
    /// </remarks>
    private const double Lines = 12;

    /// <summary>How long the hum bar takes to cross the face, in seconds.</summary>
    private const double Roll = 4.7;

    /// <summary>How far the glass reaches, measured on the face the rim is read on.</summary>
    private const double Glass = 1.25;

    /// <summary>
    /// How much harder the rim bows than the picture, which is what rounds it:
    /// the bow carries the corners furthest out, so they are what the glass cuts
    /// off first.
    /// </summary>
    private const double Rim = 0.35;

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

        // The glass: both axes pushed out by the square of the radius, which is
        // what everything drawn on the face is measured against from here on.
        var bulge = Formula(b, $"1 + {N(Bulge)} * (a * a + b * b)");
        var bowedX = Formula(b, "a * b");
        var faceY = Formula(b, "a * b");

        // The hum bar, crossing the face bottom to top. It lifts what it passes
        // over and drags the lines under it sideways.
        var band = Formula(b, $"2 * fract(a / {N(Roll)}) - 1");
        var hum = Formula(b, "exp(-18 * (a - b) * (a - b))");

        // The bar's drag, and a fine per-line wobble that never settles.
        var faceX = Formula(b, "a + c * 0.055 * sin(d * 11) + sin(b * 34 + d * 9) * 0.0035");

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

        // The raster, the snow and the mains, as one number the emitted light is
        // multiplied by: the scan line, a grain that boils, the hum bar's lift and
        // a flutter a little either side of seven a second.
        var scan = Formula(b, $"0.7 + 0.3 * cos(a * {N(Lines / 2)} * tau)");
        var boil = Formula(b, "a * 14");
        var snow = b.Add("pattern.clouds", (3, 10f));
        var lift = Formula(b, "a * (0.89 + b * 0.22) * (1 + c * 0.28) * (1 + sin(d * 46) * 0.02)");

        var emitted = b.Add("color.gain");

        // The corners of a tube, which is the one fault that is the glass rather
        // than the beam, so it is measured on the bowed face too.
        var face = b.Add("color.vignette", (3, 0.55f), (4, 1.6f), (5, 0.22f));

        // The surface the rest of it is drawn on, so the box reads as part of the
        // dialog rather than as a black tile cut into it. Added on top rather
        // than put underneath, which for light laid on a dark is the same sum,
        // and it gives the trails a floor to fade to instead of black.
        var ground = b.Add(
            "color.ink",
            (2, Colors.Panel.R / 255f),
            (3, Colors.Panel.G / 255f),
            (4, Colors.Panel.B / 255f));

        // The edge of the glass, and the dark of the set beyond it.
        var onGlass = Formula(
            b,
            $"(1 - smoothstep({N(Glass - 0.04)}, {N(Glass)}, "
            + $"abs(a) * (1 + {N(Rim)} * (a * a + b * b))))"
            + $" * (1 - smoothstep({N(Glass - 0.04)}, {N(Glass)}, "
            + $"abs(b) * (1 + {N(Rim)} * (a * a + b * b))))");

        var set = b.Add("color.gain");

        // Everything is drawn before the Trails rather than over it: what it
        // reads back is the frame, so a color laid on afterwards is added to its
        // own echo every frame until it is white.
        var trail = b.Add("feedback.trails", (7, 0.985f));

        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(coord, NodeCatalog.CoordXPort, bulge, 0)
         .Wire(coord, NodeCatalog.CoordYPort, bulge, 1)
         .Wire(coord, NodeCatalog.CoordXPort, bowedX, 0)
         .Wire(bulge, 0, bowedX, 1)
         .Wire(coord, NodeCatalog.CoordYPort, faceY, 0)
         .Wire(bulge, 0, faceY, 1)

         .Wire(clock, 0, band, 0)
         .Wire(faceY, 0, hum, 0)
         .Wire(band, 0, hum, 1)

         .Wire(bowedX, 0, faceX, 0)
         .Wire(faceY, 0, faceX, 1)
         .Wire(hum, 0, faceX, 2)
         .Wire(clock, 0, faceX, 3)

         .Wire(clock, 0, phase, 0)
         .Wire(phase, 0, half, 0)
         .Wire(phase, 0, along, 0)
         .Wire(along, 0, beamX, 0)
         .Wire(half, 0, beamX, 1)
         .Wire(along, 0, beamY, 0)
         .Wire(half, 0, beamY, 1)

         .Wire(faceX, 0, awayX, 0)
         .Wire(beamX, 0, awayX, 1)
         .Wire(faceY, 0, awayY, 0)
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
         .Wire(faceX, 0, bar, 0)
         .Wire(faceY, 0, bar, 1)
         .Wire(bar, 0, retrace, 1)

         .Wire(faceY, 0, scan, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(boil, 0, snow, 2)
         .Wire(scan, 0, lift, 0)
         .Wire(snow, 0, lift, 1)
         .Wire(hum, 0, lift, 2)
         .Wire(clock, 0, lift, 3)

         .Wire(retrace, 0, emitted, 0)
         .Wire(lift, 0, emitted, 1)
         .Wire(emitted, 0, face, 0)
         .Wire(faceX, 0, face, 1)
         .Wire(faceY, 0, face, 2)

         .Wire(face, 0, ground, 0)

         .Wire(bowedX, 0, onGlass, 0)
         .Wire(faceY, 0, onGlass, 1)
         .Wire(ground, 0, set, 0)
         .Wire(onGlass, 0, set, 1)

         .Wire(set, 0, trail, 0)
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
