using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The wiring a big preset says hundreds of times, said once: a module added and
/// its operands wired in the same breath, so a line of the preset reads as the
/// arithmetic it is rather than as three statements about sockets.
/// </summary>
/// <remarks>
/// Nothing here is a new kind of module. Every method adds exactly the catalogue
/// module its name stands for and returns it, so what a preset builds with these
/// is what it would have built with <see cref="PatchBuilder.Add(string, ValueTuple{int, float}[])"/>
/// and <see cref="PatchBuilder.Wire"/> by hand, module for module.
/// </remarks>
internal abstract class PresetBench(ModuleCatalog modules)
{
    protected readonly PatchBuilder b = new(modules);

    /// <summary>How many modules were already in a box when the last one was closed.</summary>
    private int boxed;

    /// <summary>
    /// Everything added since the last box, drawn as one. A module made inside a
    /// helper lands in the box that was open when it was made, which is the part of
    /// the patch it belongs to.
    /// </summary>
    protected void Box(string name, params NodeInstance[] except)
    {
        b.Group(name, [.. b.Patch.Nodes.Skip(boxed).Except(except)]);
        boxed = b.Patch.Nodes.Count;
    }

    /// <summary>
    /// A module of two operands, the first on the first socket. <paramref name="from"/>
    /// and <paramref name="second"/> are the outputs read, for the few sources whose
    /// first is not the one wanted: a Sequencer's gate, a Euclid's hit, a Fill's outline.
    /// </summary>
    protected NodeInstance Wired(string type, NodeInstance a, NodeInstance c, int from = 0, int second = 0)
    {
        var node = b.Add(type);
        b.Wire(a, from, node, 0).Wire(c, second, node, 1);
        return node;
    }

    /// <summary>The same with the second operand a number on the knob.</summary>
    protected NodeInstance Knobbed(string type, NodeInstance a, float by, int from = 0)
    {
        var node = b.Add(type, (1, by));
        b.Wire(a, from, node, 0);
        return node;
    }

    protected NodeInstance Through(string type, NodeInstance a, int from = 0)
    {
        var node = b.Add(type);
        b.Wire(a, from, node, 0);
        return node;
    }

    protected NodeInstance Times(NodeInstance a, float by, int from = 0) => Knobbed("math.mul", a, by, from);

    protected NodeInstance Plus(NodeInstance a, float by, int from = 0) => Knobbed("math.add", a, by, from);

    protected NodeInstance Power(NodeInstance a, float by) => Knobbed("math.pow", a, by);

    protected NodeInstance Sum(NodeInstance a, NodeInstance c) => Wired("math.add", a, c);

    protected NodeInstance Less(NodeInstance a, NodeInstance c) => Wired("math.sub", a, c);

    /// <summary>A number less a signal, which is how an envelope is turned over.</summary>
    protected NodeInstance From(float whole, NodeInstance a, int from = 0)
    {
        var node = b.Add("math.sub", (0, whole));
        b.Wire(a, from, node, 1);
        return node;
    }

    protected NodeInstance Product(NodeInstance a, NodeInstance c, int second = 0) =>
        Wired("math.mul", a, c, 0, second);

    protected NodeInstance Sine(NodeInstance a) => Through("math.sin", a);

    protected NodeInstance Floor(NodeInstance a) => Through("math.floor", a);

    protected NodeInstance Fraction(NodeInstance a) => Through("math.fract", a);

    protected NodeInstance Size(NodeInstance a, int from = 0) => Through("math.abs", a, from);

    /// <summary>A Remap: one range onto another, neither end clamped.</summary>
    protected NodeInstance Span(
        NodeInstance a, float inLow, float inHigh, float outLow, float outHigh, int from = 0)
    {
        var node = b.Add("math.remap", (1, inLow), (2, inHigh), (3, outLow), (4, outHigh));
        b.Wire(a, from, node, 0);
        return node;
    }

    /// <summary>A Smoothstep: nothing under <paramref name="from"/>, one over <paramref name="to"/>.</summary>
    protected NodeInstance Rises(NodeInstance a, float from, float to, int output = 0)
    {
        var node = b.Add("math.smoothstep", (0, from), (1, to));
        b.Wire(a, output, node, 2);
        return node;
    }

    /// <summary>The Stroke's second output: how far through the stroke it is.</summary>
    protected const int StrokePhase = 1;

    /// <summary>The Fade's second output: the fade alone, without what it fades.</summary>
    protected const int FadeGate = 1;

    /// <summary>
    /// A Stroke: what is left of each <paramref name="rate"/>th of the position, to
    /// the power <paramref name="curve"/> — an envelope that needs no trigger and that
    /// the screen can read as well as the speakers can.
    /// </summary>
    protected NodeInstance Stroke(NodeInstance position, float rate, float curve, float offset = 0f)
    {
        var node = b.Add("flyback.voice.stroke", (1, rate), (2, offset), (3, curve));
        b.Wire(position, 0, node, 0);
        return node;
    }

    /// <summary>
    /// A Fade: <paramref name="a"/> let through as <paramref name="level"/> passes from
    /// <paramref name="from"/> to <paramref name="to"/>, which is how a part enters.
    /// </summary>
    protected NodeInstance Enters(NodeInstance a, NodeInstance level, float from, float to)
    {
        var node = Enters(level, from, to);
        b.Wire(a, 0, node, 0);
        return node;
    }

    /// <summary>
    /// The same with nothing to fade yet, for a part whose entry is wanted before the
    /// part has been built. Wire the part into socket nought when it has.
    /// </summary>
    protected NodeInstance Enters(NodeInstance level, float from, float to)
    {
        var node = b.Add("flyback.voice.fade", (2, from), (3, to));
        b.Wire(level, 0, node, 1);
        return node;
    }

    /// <summary>The Drum's pitch, for one whose resting pitch is a wire.</summary>
    protected const int DrumPitch = 2;

    /// <summary>A Drum: a sine at <paramref name="pitch"/>, swept and leveled by one envelope.</summary>
    protected NodeInstance Drum(NodeInstance level, float pitch, float sweep, float bend, float drive)
    {
        var node = b.Add("flyback.voice.drum", (2, pitch), (3, sweep), (4, bend), (5, drive));
        b.Wire(level, 0, node, 1);
        return node;
    }

    /// <summary>A Wander: a slow random value between two ends, on a lane of its own.</summary>
    protected NodeInstance Wander(float rate, float seed, float low = 0f, float high = 1f) =>
        b.Add("flyback.voice.wander", (1, rate), (2, seed), (3, low), (4, high));

    /// <summary>The Trails module and the knobs a preset sets on one, after its picture and its position.</summary>
    protected const string TrailsType = "feedback.trails";

    protected const int TrailsZoom = 3;

    protected const int TrailsAngle = 4;

    protected const int TrailsDx = 5;

    protected const int TrailsPersist = 7;

    protected const string DeskType = "math.desk";

    /// <summary>The Desk's trim, after its four channels of left, right and level and its two bus inputs.</summary>
    protected const int DeskTrim = 14;

    /// <summary>
    /// One channel of a Desk, counted from one: a level, and a left that is also the
    /// right unless <paramref name="right"/> is given. <paramref name="from"/> and
    /// <paramref name="rightFrom"/> are the outputs read, for a Chorus or a Reverb
    /// whose second output is its other side.
    /// </summary>
    protected void Channel(
        NodeInstance desk, int channel, float level,
        NodeInstance left, NodeInstance? right = null, int from = 0, int rightFrom = 0)
    {
        var at = (channel - 1) * 3;

        desk.InputValues[at + 2] = level;
        b.Wire(left, from, desk, at);
        if (right is not null) b.Wire(right, rightFrom, desk, at + 1);
    }

    /// <summary>
    /// Desks made one: each hands its buses to the next, so the last is the master
    /// and the only one whose trim and rails are heard.
    /// </summary>
    protected NodeInstance Chained(params NodeInstance[] desks)
    {
        const int busOut = 2;
        const int busIn = 12;

        for (var i = 1; i < desks.Length; i++)
            b.Wire(desks[i - 1], busOut, desks[i], busIn)
             .Wire(desks[i - 1], busOut + 1, desks[i], busIn + 1);

        return desks[^1];
    }

    /// <summary>A sine at a frequency and a level, both wires.</summary>
    protected NodeInstance Tone(NodeInstance hz, NodeInstance level)
    {
        var tone = b.Add("osc.sine");
        b.Wire(hz, 0, tone, 1).Wire(level, 0, tone, 3);
        return tone;
    }

    /// <summary>
    /// Struck metal out of two sines: one at the pitch, and one at a ratio above it
    /// leaning on the first one's phase. How hard it leans is the stroke, so the note
    /// is bright when it is hit and pure by the time it has rung — which is the whole
    /// character of a bell, for two oscillators.
    /// </summary>
    protected NodeInstance Bell(NodeInstance hz, NodeInstance stroke, float ratio, float index)
    {
        var partial = Tone(Times(hz, ratio), Times(stroke, index));
        var bell = Tone(hz, stroke);
        b.Wire(partial, 0, bell, 2);
        return bell;
    }
}
