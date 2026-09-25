using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A game: walls fall down seven lanes, each with one gap, and the key under the
/// gap is the lane you have to be in when the wall arrives.
/// </summary>
/// <remarks>
/// Z to M are the lanes and the lane is the last note struck, so a clean run
/// plays the gaps' tune. Black keys and other octaves fall to the nearest white key.
/// <para>
/// Three loops of one (ADR-0075) hold the state: Keys (last pitch and gate, and
/// whether a note is new), Over (when the run ended, or -1) and Start (when it
/// began). Everything else is derived from those and the clock.
/// </para>
/// <para>
/// A cell holds ±16, so times wrap at 512 seconds. Gaps are a quadratic residue
/// modulo 997, in whole numbers, so picture and sound agree.
/// </para>
/// </remarks>
internal static class DodgePreset
{
    public const string Name = "Dodge";

    /// <summary>The lanes, left to right: the bottom row's white keys.</summary>
    private static readonly string[] Keys = ["Z", "X", "C", "V", "B", "N", "M"];

    /// <summary>Which gap row <c>a</c> has: a lane from 0 to 6.</summary>
    private const string Gap =
        "min(floor(mod(mod(mod(floor(a), 997) * mod(floor(a), 997), 997) * 73 + mod(floor(a), 997) * 151 + 17, 997) * 7 / 997), 6)";

    /// <summary>A lane's note above C, in C major.</summary>
    private static string Degree(string lane) => $"(2 * {lane} - step(2.5, {lane}))";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var clock = b.Add(NodeCatalog.TimeTypeId);
        var here = b.Add(NodeCatalog.CoordTypeId);
        var keyboard = b.Add(NodeCatalog.MidiTypeId);
        const int pitch = 0, gate = 1, velocity = 2, trigger = 3;

        // Last note and gate in whole numbers, and 256 on top when this one was struck.
        var keys = Formula(
            b,
            "(floor(b + 0.5) + 128 * step(0.5, c) + 256 * step(0.5, c) * max(1 - step(128, mod(floor(32 * a + 0.5), 256)), "
            + "step(0.5, abs(floor(b + 0.5) - mod(floor(32 * a + 0.5), 256) + 128 * step(128, mod(floor(32 * a + 0.5), 256)))))) / 32");
        b.Wire(keys, 0, keys, 0).Wire(keyboard, pitch, keys, 1).Wire(keyboard, gate, keys, 2);

        // When the run ended, over 32; -1 while it runs. Starts ended, at nought.
        var over = Formula(
            b,
            "mix(mix(a, mod(d, 512) / 32, (1 - step(-0.5, a)) * c), -1, "
            + "step(-0.5, a) * step(0.8, mod(d - 32 * a, 512)) * step(8, b))");

        // When the run began, over 33 and a sixty-fourth, negative while Over says it is not running.
        var start = Formula(
            b,
            "mix(-abs(a), mix(mod(c, 512) / 33 + 1 / 64, a, step(1 / 128, a)), 1 - step(-0.5, b))");

        // Seconds into the run, held where it ended.
        var run = Formula(
            b,
            "mix(step(-0.5, b) * mod(32 * b - 33 * (abs(a) - 1 / 64), 512), "
            + "mod(c - 33 * (abs(a) - 1 / 64), 512), step(1 / 128, a))");

        // Rows the walls have come, three short of the player at the start: 1.2 a second, rising to 4.
        var rows = Formula(b, "1.2 * min(a, 70) + 0.02 * min(a, 70) * min(a, 70) + 4 * (a - min(a, 70)) - 3");

        // The lane: the last note's nearest white key, or the middle before one is struck.
        var lane = Formula(b, "mix(3, floor(mod(floor(a + 0.5), 12) * 7 / 12 + 0.5), step(0.001, b))");

        // In a wall, and not in its gap.
        var hit = Formula(b, $"step(0, a) * (1 - step(0.25, fract(a))) * step(0.5, abs(b - {Gap}))");

        b.Wire(over, 0, over, 0).Wire(keys, 0, over, 1).Wire(hit, 0, over, 2).Wire(clock, 0, over, 3)
         .Wire(start, 0, start, 0).Wire(over, 0, start, 1).Wire(clock, 0, start, 2)
         .Wire(start, 0, run, 0).Wire(over, 0, run, 1).Wire(clock, 0, run, 2)
         .Wire(run, 0, rows, 0)
         .Wire(keyboard, pitch, lane, 0).Wire(keyboard, velocity, lane, 1)
         .Wire(rows, 0, hit, 0).Wire(lane, 0, hit, 1);

        b.Group("Game", keys, over, start, run, rows, lane, hit);

        // --- Picture. Seven lanes 0.26 wide across the middle, the player at -0.72, a row every half.

        // Dimmed between runs. Drawn first so the Output reaches Over before anything else
        // in the game's loop, which puts the cut on the wires out of Over: the loop's other
        // values run past what a cell holds.
        var field = Formula(b, "(0.05 - 0.025 * step(-0.5, b)) * step(0, a + 0.91) * (1 - step(0, a - 0.91))");
        b.Wire(here, NodeCatalog.CoordXPort, field, 0).Wire(over, 0, field, 1);

        var walls = Formula(
            b,
            "step(-0.8, c) * step(0, a + (c + 0.72) / 0.5) * (1 - step(0.25, fract(a + (c + 0.72) / 0.5))) "
            + "* step(0, b + 0.91) * (1 - step(0, b - 0.91)) "
            + $"* step(0.5, abs(floor((b + 0.91) / 0.26) - {Gap.Replace("a", "(a + (c + 0.72) / 0.5)")}))");
        b.Wire(rows, 0, walls, 0).Wire(here, NodeCatalog.CoordXPort, walls, 1).Wire(here, NodeCatalog.CoordYPort, walls, 2);

        var player = Formula(
            b,
            "1 - smoothstep(0, 0.006, max(abs(b - 0.26 * a + 0.78) - 0.09, abs(c + 0.72) - 0.045))");
        b.Wire(lane, 0, player, 0).Wire(here, NodeCatalog.CoordXPort, player, 1).Wire(here, NodeCatalog.CoordYPort, player, 2);

        // The key under each lane: one Text, its line chosen by the lane the pixel is in.
        var column = Formula(b, "clamp(floor((a + 0.91) / 0.26), 0, 6)");
        var acrossLane = Formula(b, "a + 0.78 - 0.26 * b");
        var lower = Formula(b, "a + 0.88");
        var letters = TextModule.WithFont(TextModule.WithLines(b.Add(TextModule.TypeId, (2, 0.06f)), Keys), BitmapFont.Tiny);
        var labels = b.Add(FillModule.TypeId, (1, 0.004f));
        b.Wire(here, NodeCatalog.CoordXPort, column, 0)
         .Wire(here, NodeCatalog.CoordXPort, acrossLane, 0).Wire(column, 0, acrossLane, 1)
         .Wire(here, NodeCatalog.CoordYPort, lower, 0)
         .Wire(acrossLane, 0, letters, 0).Wire(lower, 0, letters, 1).Wire(column, 0, letters, TextModule.LinePort)
         .Wire(letters, 0, labels, 0);

        // Walls cleared, in three digits along the top: one Text again, its line the digit.
        var score = Formula(b, "max(0, floor(a - 0.25) + 1)");
        var place = Formula(b, "clamp(floor((a + 0.15) / 0.1), 0, 2)");
        var digit = Formula(b, "floor(a / pow(10, 2 - b))");
        var acrossDigit = Formula(b, "a + 0.1 - 0.1 * b");
        var upper = Formula(b, "a - 0.86");
        var digits = TextModule.WithLines(b.Add(TextModule.TypeId, (2, 0.09f)), [.. Enumerable.Range(0, 10).Select(n => $"{n}")]);
        var scored = b.Add(FillModule.TypeId, (1, 0.004f));
        b.Wire(rows, 0, score, 0)
         .Wire(here, NodeCatalog.CoordXPort, place, 0)
         .Wire(score, 0, digit, 0).Wire(place, 0, digit, 1)
         .Wire(here, NodeCatalog.CoordXPort, acrossDigit, 0).Wire(place, 0, acrossDigit, 1)
         .Wire(here, NodeCatalog.CoordYPort, upper, 0)
         .Wire(acrossDigit, 0, digits, 0).Wire(upper, 0, digits, 1).Wire(digit, 0, digits, TextModule.LinePort)
         .Wire(digits, 0, scored, 0);

        // Between runs: what to do, blinking, and a red flash that fades from the moment it ended.
        var title = TextModule.WithLines(b.Add(TextModule.TypeId, (2, 0.1f)), "PRESS A KEY", "GAME OVER");
        var titleLine = Formula(b, "step(1 / 1024, a)");
        var titleY = Formula(b, "a - 0.1");
        var titled = b.Add(FillModule.TypeId, (1, 0.004f));
        var showing = Formula(b, "a * step(-0.5, b) * step(0.3, fract(c * 1.5))");
        var flash = Formula(b, "0.6 * step(1 / 1024, b) * step(-0.5, b) * exp(-4 * mod(a - 32 * b, 512))");
        b.Wire(over, 0, titleLine, 0)
         .Wire(here, NodeCatalog.CoordYPort, titleY, 0)
         .Wire(titleY, 0, title, 1).Wire(titleLine, 0, title, TextModule.LinePort)
         .Wire(title, 0, titled, 0)
         .Wire(titled, 0, showing, 0).Wire(over, 0, showing, 1).Wire(clock, 0, showing, 2)
         .Wire(clock, 0, flash, 0).Wire(over, 0, flash, 1);

        var picture = Ink(b, null, field, 0.4f, 0.5f, 1f);
        picture = Ink(b, picture, walls, 0.95f, 0.25f, 0.75f);
        picture = Ink(b, picture, player, 0.3f, 1f, 0.9f);
        picture = Ink(b, picture, labels, 0.45f, 0.5f, 0.7f);
        picture = Ink(b, picture, scored, 1f, 1f, 1f);
        picture = Ink(b, picture, showing, 1f, 0.85f, 0.3f);
        picture = Ink(b, picture, flash, 1f, 0.1f, 0.15f);

        b.Group("Picture", field, walls, player, column, acrossLane, lower, letters, labels,
            score, place, digit, acrossDigit, upper, digits, scored,
            title, titleLine, titleY, titled, showing, flash);

        // --- Sound. The key plucked, the gap chimed as each wall passes, and a thud when one does not.

        var note = b.Add("audio.note");
        var pluck = b.Add(NodeCatalog.StringTypeId, (3, 0.6f), (4, 0.45f));
        b.Wire(keyboard, pitch, note, 0)
         .Wire(note, 0, pluck, 2).Wire(keyboard, trigger, pluck, 1);

        var chimeHz = Formula(b, $"440 * pow(2, (72 + {Degree(Gap)} - 69) / 12)");
        var chimeLevel = Formula(b, "step(0, a) * (1 - step(-0.5, b)) * exp(-7 * fract(a))");
        var chime = b.Add("osc.sine");
        b.Wire(rows, 0, chimeHz, 0)
         .Wire(rows, 0, chimeLevel, 0).Wire(over, 0, chimeLevel, 1)
         .Wire(chimeHz, 0, chime, 1).Wire(chimeLevel, 0, chime, 3);

        var thudHz = Formula(b, "40 + 120 * exp(-8 * mod(a - 32 * b, 512))");
        var thudLevel = Formula(b, "step(1 / 1024, b) * step(-0.5, b) * exp(-4 * mod(a - 32 * b, 512))");
        var thud = b.Add("osc.sine");
        b.Wire(clock, 0, thudHz, 0).Wire(over, 0, thudHz, 1)
         .Wire(clock, 0, thudLevel, 0).Wire(over, 0, thudLevel, 1)
         .Wire(thudHz, 0, thud, 1).Wire(thudLevel, 0, thud, 3);

        var mixed = Formula(b, "0.5 * a + 0.2 * b + 0.7 * c");
        b.Wire(pluck, 0, mixed, 0).Wire(chime, 0, mixed, 1).Wire(thud, 0, mixed, 2);

        b.Group("Sound", note, pluck, chimeHz, chimeLevel, chime, thudHz, thudLevel, thud, mixed);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.8f));
        b.Wire(picture, 0, output, NodeCatalog.OutputColorPort)
         .Wire(mixed, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(mixed, 0, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }

    private static NodeInstance Formula(PatchBuilder b, string formula)
    {
        var node = b.Add(NodeCatalog.ExpressionTypeId);
        node.SetState("expression", new JsonObject { ["formula"] = formula });
        return node;
    }

    private static NodeInstance Ink(PatchBuilder b, NodeInstance? under, NodeInstance mask, float red, float green, float blue)
    {
        var ink = b.Add("color.ink", (2, red), (3, green), (4, blue));
        if (under is not null) b.Wire(under, 0, ink, 0);
        b.Wire(mask, 0, ink, 1);
        return ink;
    }
}
