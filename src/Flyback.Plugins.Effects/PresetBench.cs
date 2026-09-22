using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The wiring a big preset says hundreds of times, said once: a module added and
/// its operands wired in the same breath, so a line of the preset reads as the
/// arithmetic it is rather than as three statements about sockets.
/// </summary>
/// <remarks>
/// Nothing here is a new kind of module. Every method adds exactly the catalog
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

    /// <summary>An output to read into a socket: a module's first, unless another is named.</summary>
    protected readonly record struct Read(NodeInstance Node, int Port = 0)
    {
        public static implicit operator Read(NodeInstance node) => new(node);
    }

    /// <summary>
    /// An Expression: <paramref name="formula"/> over what is wired into its sockets,
    /// <paramref name="sockets"/> going to a, b, c and d in that order.
    /// </summary>
    /// <remarks>
    /// Spell a number a C# constant was folded from the way it was folded —
    /// <c>1 / 45</c> rather than <c>0.0222</c>, <c>45 * 2 * pi</c> left to right — and
    /// the formula folds it to the same float, so the Expression is the modules it
    /// replaces to the bit.
    /// </remarks>
    protected NodeInstance Formula(string formula, params Read[] sockets)
    {
        var node = b.Add(NodeCatalog.ExpressionTypeId);
        node.SetState("expression", new JsonObject { ["formula"] = formula });

        for (var socket = 0; socket < sockets.Length; socket++)
            b.Wire(sockets[socket].Node, sockets[socket].Port, node, socket);

        return node;
    }

    /// <summary>A Remap: one range onto another, neither end clamped.</summary>
    protected NodeInstance Span(
        NodeInstance a, float inLow, float inHigh, float outLow, float outHigh, int from = 0)
    {
        var node = b.Add("math.remap", (1, inLow), (2, inHigh), (3, outLow), (4, outHigh));
        b.Wire(a, from, node, 0);
        return node;
    }

    /// <summary>Which of a Duck's outputs is the level it applies.</summary>
    protected const int DuckGain = 2;

    /// <summary>
    /// A Duck keyed by <paramref name="key"/>, for its <see cref="DuckGain"/>: one
    /// while the key is quiet, down by <paramref name="depth"/> while it is full.
    /// </summary>
    /// <remarks>
    /// Its times rest at their quickest, so a key that is already an envelope ducks
    /// in the envelope's own shape.
    /// </remarks>
    protected NodeInstance Ducking(
        NodeInstance key, float depth, float attack = -4f, float release = -4f, float full = 1f, int from = 0)
    {
        var node = b.Add(NodeCatalog.DuckTypeId, (3, depth), (4, full), (5, attack), (6, release));
        b.Wire(key, from, node, 2);
        return node;
    }

    /// <summary>A Smoothstep: nothing under <paramref name="from"/>, one over <paramref name="to"/>.</summary>
    protected NodeInstance Rises(NodeInstance a, float from, float to, int output = 0)
    {
        var node = b.Add("math.smoothstep", (0, from), (1, to));
        b.Wire(a, output, node, 2);
        return node;
    }

    /// <summary>A knob on the patch's panel, resting at <paramref name="at"/> of its turn.</summary>
    protected PatchControl Panel(string name, float at) => b.Patch.AddControl(name, at);

    /// <summary>
    /// A socket that follows a panel knob from <paramref name="low"/> to
    /// <paramref name="high"/>, left resting where the knob rests. A
    /// <paramref name="low"/> over the <paramref name="high"/> turns the knob round.
    /// </summary>
    protected static void Follows(NodeInstance node, int port, PatchControl knob, float low, float high)
    {
        var link = new ControlLink(knob.Id, low, high);

        node.InputValues[port] = link.At(knob.Value);
        ControlMap.Link(node, port, link);
    }

    /// <summary>
    /// A panel knob as a signal, for what a knob has to reach through arithmetic: a
    /// Value whose one socket follows it.
    /// </summary>
    protected NodeInstance Dial(PatchControl knob, float low, float high)
    {
        var dial = b.Add("value");
        Follows(dial, 0, knob, low, high);
        return dial;
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
    /// <paramref name="output"/> is the output of <paramref name="a"/> read, for a
    /// Euclid's stroke.
    /// </summary>
    protected NodeInstance Enters(NodeInstance a, NodeInstance level, float from, float to, int output = 0)
    {
        var node = Enters(level, from, to);
        b.Wire(a, output, node, 0);
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

    /// <summary>The Bell's index, for one whose brightness is a wire.</summary>
    protected const int BellIndex = 4;

    /// <summary>
    /// A Bell: struck metal at <paramref name="hz"/>, with an overtone at
    /// <paramref name="ratio"/> times the pitch that is <paramref name="index"/> strong
    /// when the stroke is at its height and gone by the time it has rung.
    /// </summary>
    protected NodeInstance Bell(NodeInstance hz, NodeInstance stroke, float ratio, float index, int from = 0)
    {
        var bell = b.Add("flyback.voice.bell", (3, ratio), (BellIndex, index));
        b.Wire(hz, from, bell, 1).Wire(stroke, 0, bell, 2);
        return bell;
    }

    /// <summary>The Hiss's cutoff, for one that is swept.</summary>
    protected const int HissCutoff = 2;

    /// <summary>
    /// A Hiss: noise through one band of a filter, as loud as <paramref name="level"/>,
    /// or wide open where there is none and whatever follows plays it.
    /// <paramref name="band"/> is low, band or high, and <paramref name="noise"/>
    /// white or pink.
    /// </summary>
    protected NodeInstance Hiss(
        NodeInstance? level, float cutoff, float resonance, string band,
        float gain = 1f, string noise = "white", float seed = 0f)
    {
        var hiss = b.Add("flyback.voice.hiss", (HissCutoff, cutoff), (3, resonance), (4, gain), (5, seed));
        hiss.SetState("hiss", new JsonObject { ["noise"] = noise, ["band"] = band });

        if (level is null) hiss.InputValues[1] = 1f;
        else b.Wire(level, 0, hiss, 1);

        return hiss;
    }

    /// <summary>The Echo's feedback, for one whose repeats are a wire, and its right side.</summary>
    protected const int EchoFeedback = 4;

    protected const int EchoRight = 1;

    /// <summary>
    /// An Echo: two taps counted in sixteenths of <paramref name="tempo"/>, in a row
    /// unless <paramref name="sideBySide"/>.
    /// </summary>
    protected NodeInstance Echo(
        NodeInstance from, NodeInstance tempo, float left, float right, float feedback, float mix,
        bool sideBySide = false)
    {
        var echo = b.Add(EchoModule.TypeId, (2, left), (3, right), (EchoFeedback, feedback), (5, mix));

        if (sideBySide)
            echo.SetState(EchoModule.StateKey, new JsonObject { [EchoModule.TapsKey] = EchoModule.SideBySide });

        b.Wire(from, 0, echo, 0).Wire(tempo, 0, echo, 1);
        return echo;
    }

    /// <summary>
    /// A Tune: <paramref name="note"/> moved by <paramref name="transpose"/> semitones,
    /// snapped to <paramref name="scale"/>, as a frequency.
    /// </summary>
    protected NodeInstance InKey(NodeInstance note, int[] scale, float transpose = 0f, int from = 0)
    {
        var tune = b.Add("audio.tune", (1, transpose));
        ScaleExtra.Set(tune, scale);
        b.Wire(note, from, tune, 0);
        return tune;
    }

    /// <summary>The Transform's knobs, after its position.</summary>
    protected const string TransformType = "space.transform";

    protected const int TransformZoom = 2;

    protected const int TransformAngle = 3;

    /// <summary>A Transform that turns before it zooms, which is a Rotate into a Scale.</summary>
    protected NodeInstance TurnedThenZoomed(params (int Port, float Value)[] knobs)
    {
        var placed = b.Add(TransformType, knobs);
        placed.SetState(
            NodeCatalog.TransformStateKey,
            new JsonObject { [NodeCatalog.TransformOrderKey] = NodeCatalog.TurnThenZoom });
        return placed;
    }

    /// <summary>
    /// An Ink: one color laid on <paramref name="under"/> as light, through
    /// <paramref name="mask"/>.
    /// </summary>
    protected NodeInstance Ink(NodeInstance? under, NodeInstance mask, float red, float green, float blue)
    {
        var ink = b.Add("color.ink", (2, red), (3, green), (4, blue));
        if (under is not null) b.Wire(under, 0, ink, 0);
        b.Wire(mask, 0, ink, 1);
        return ink;
    }

    /// <summary>The Vignette's second output: the darkening alone.</summary>
    protected const int VignetteShade = 1;

    /// <summary>A Vignette: the corners of <paramref name="picture"/> darkened.</summary>
    protected NodeInstance Vignette(NodeInstance? picture, float from, float to, float dark)
    {
        var vignette = b.Add("color.vignette", (3, from), (4, to), (5, dark));
        if (picture is not null) b.Wire(picture, 0, vignette, 0);
        return vignette;
    }

    /// <summary>The Euclid's envelope, and the knob that bends it.</summary>
    protected const int EuclidStroke = 3;

    protected const int EuclidCurve = 6;
}
