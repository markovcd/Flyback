namespace Flyback.Core.Graph;

/// <summary>Patches that ship with the synth, so it never opens on a blank canvas.</summary>
public static class Presets
{
    public static IReadOnlyList<(string Name, Func<Patch> Build)> All =>
    [
        ("Plasma", Plasma),
        ("Kaleidoscope", Kaleidoscope),
        ("Feedback tunnel", FeedbackTunnel),
        ("Empty", Empty),
    ];

    public static Patch Default() => Plasma();

    /// <summary>Just an Output node with something to plug into.</summary>
    public static Patch Empty()
    {
        var b = new PatchBuilder();
        b.Add(NodeCatalog.OutputTypeId, 640, 260);
        return b.Patch;
    }

    /// <summary>Two sine fields crossed and read as hue — the "hello world" of video synths.</summary>
    public static Patch Plasma()
    {
        var b = new PatchBuilder();

        var coord = b.Add("coord", 40, 200);
        var time = b.Add("time", 40, 400, (0, 0.2f));

        // Sine along x, and a second along y whose phase drifts with time.
        var horizontal = b.Add("osc.sine", 260, 120, (1, 1.5f));
        var vertical = b.Add("osc.sine", 260, 300, (1, 1.1f));

        var sum = b.Add("math.add", 500, 200);
        var hue = b.Add("math.remap", 660, 200, (1, -2f), (2, 2f), (3, 0f), (4, 1f));
        var colour = b.Add("colour.hsv", 860, 200, (1, 0.85f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1060, 220);

        b.Wire(coord, 0, horizontal, 0)
         .Wire(coord, 1, vertical, 0)
         .Wire(time, 0, vertical, 2)
         .Wire(horizontal, 0, sum, 0)
         .Wire(vertical, 0, sum, 1)
         .Wire(sum, 0, hue, 0)
         .Wire(hue, 0, colour, 0)
         .Wire(colour, 0, output, 0);

        return b.Patch;
    }

    /// <summary>Rotating wedges filled with noise that boils over time.</summary>
    public static Patch Kaleidoscope()
    {
        var b = new PatchBuilder();

        var coord = b.Add("coord", 40, 220);
        var spin = b.Add("time", 40, 60, (0, 0.15f));
        var drift = b.Add("time", 40, 460, (0, 0.3f));

        var rotate = b.Add("space.rotate", 260, 160);
        var fold = b.Add("space.kaleidoscope", 470, 200, (2, 6f));
        var noise = b.Add("pattern.noise", 680, 240, (3, 2.5f));
        var colour = b.Add("colour.hsv", 890, 240, (1, 0.9f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1090, 260);

        b.Wire(coord, 0, rotate, 0)
         .Wire(coord, 1, rotate, 1)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, fold, 0)
         .Wire(rotate, 1, fold, 1)
         .Wire(fold, 0, noise, 0)
         .Wire(fold, 1, noise, 1)
         .Wire(drift, 0, noise, 2)
         .Wire(noise, 0, colour, 0)
         .Wire(colour, 0, output, 0);

        return b.Patch;
    }

    /// <summary>
    /// The camera-pointed-at-its-own-monitor patch: each frame is re-read
    /// slightly rotated, scaled and dimmed, with fresh rings fed in on top.
    /// </summary>
    public static Patch FeedbackTunnel()
    {
        var b = new PatchBuilder();

        var coord = b.Add("coord", 40, 240);
        var spin = b.Add("time", 40, 60, (0, 0.08f));
        var pulse = b.Add("time", 40, 560, (0, 0.25f));

        var rotate = b.Add("space.rotate", 250, 140);
        var scale = b.Add("space.scale", 440, 160, (2, 1.05f));
        var previous = b.Add("feedback", 620, 180);
        var dim = b.Add("colour.gain", 790, 180, (1, 0.95f), (2, 0f));

        // Fresh material: bright rings that travel outward.
        var rings = b.Add("pattern.rings", 250, 460, (2, 1.5f));
        var spark = b.Add("math.smoothstep", 450, 500, (0, 0.8f), (1, 1f));
        var tint = b.Add("colour.hsv", 640, 520, (1, 1f));

        var combine = b.Add("math.max", 950, 300);
        var output = b.Add(NodeCatalog.OutputTypeId, 1130, 320);

        b.Wire(coord, 0, rotate, 0)
         .Wire(coord, 1, rotate, 1)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, scale, 0)
         .Wire(rotate, 1, scale, 1)
         .Wire(scale, 0, previous, 0)
         .Wire(scale, 1, previous, 1)
         .Wire(previous, 0, dim, 0)
         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(pulse, 0, rings, 3)
         .Wire(rings, 0, spark, 2)
         .Wire(pulse, 0, tint, 0)
         .Wire(spark, 0, tint, 2)
         .Wire(dim, 0, combine, 0)
         .Wire(tint, 0, combine, 1)
         .Wire(combine, 0, output, 0);

        return b.Patch;
    }
}
