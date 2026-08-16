using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// Every module the synth knows how to build. Each entry pairs a socket layout
/// with the ops it lowers to; adding a module here makes it appear in the
/// editor palette and compile with no other changes.
/// </summary>
/// <remarks>
/// The definitions below are the ones that ship in the engine. A plugin may add
/// more, so the lookups here read through <see cref="Current"/> — installed once
/// at startup, before any patch is compiled, and never changed after. Anything
/// that wants to reason about a catalogue that is not the running one should
/// take a <see cref="ModuleCatalog"/> rather than come here.
/// </remarks>
public static class NodeCatalog
{
    /// <summary>The provider every module in this file belongs to. Reserved.</summary>
    public static ModuleProvider BuiltInProvider { get; } = new("flyback", "Flyback");

    /// <summary>The screen sink. Paired with <see cref="AudioOutputTypeId"/> per ADR-0022.</summary>
    public const string VideoOutputTypeId = "video.output";

    /// <summary>The speaker sink. Paired with <see cref="VideoOutputTypeId"/> per ADR-0022.</summary>
    public const string AudioOutputTypeId = "audio.output";

    /// <summary>RGB, so the video sink reads three registers.</summary>
    public const int VideoChannels = 3;

    /// <summary>Stereo, so the audio sink reads two registers where video reads three.</summary>
    public const int AudioChannels = 2;

    private const float Tau = 6.283185307179586f;

    /// <summary>Just the modules that ship in the engine, with nothing added.</summary>
    public static ModuleCatalog BuiltIn { get; }

    /// <summary>The catalogue the running program uses.</summary>
    public static ModuleCatalog Current { get; private set; }

    /// <summary>
    /// Puts a composed catalogue in place. Called once during startup, after
    /// plugins have been read and before any patch exists — a module appearing
    /// or vanishing later would leave already-compiled programs describing a
    /// catalogue that no longer matches.
    /// </summary>
    public static void Install(ModuleCatalog catalog) => Current = catalog;

    public static IReadOnlyList<NodeDef> All => Current.All;

    public static IEnumerable<string> Categories => Current.Categories;

    public static NodeDef? Get(string typeId) => Current.Get(typeId);

    public static NodeDef Require(string typeId) => Current.Require(typeId);

    // --- port shorthands -----------------------------------------------------

    private static PortSpec Num(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, value, min, max);

    private static PortSpec Col(string name) => new(name, PortKind.Colour);

    /// <summary>An input that carries an earlier one through when left unpatched.</summary>
    private static PortSpec Normalled(string name, int from, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, 0f, min, max, from);

    private static PortSpec Any(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Any, value, min, max);

    /// <summary>A note number, which the editor writes out by name rather than as a number.</summary>
    private static PortSpec Pitched(string name, float value) =>
        new(name, PortKind.Scalar, value, 0f, 127f, -1, PortDisplay.Note);

    // --- emit shorthands -----------------------------------------------------

    private static NodeDef Unary(string id, string name, OpCode code, string description) => new(
        id, name, "Maths", [Any("in")], [Any("out")],
        (em, i) => [em.Unary(code, i[0])], description);

    private static NodeDef Binary(
        string id, string name, OpCode code, float defaultB, string description) => new(
        id, name, "Maths", [Any("a"), Any("b", defaultB)], [Any("out")],
        (em, i) => [em.Binary(code, i[0], i[1])], description);

    static NodeCatalog()
    {
        NodeDef[] modules =
        [
            // ---------------------------------------------------------------- output
            new NodeDef(
                VideoOutputTypeId, "Video Output", "Output",
                [Col("colour")], [],
                (_, i) => [i[0]],
                "The screen. Everything upstream of this is what you see."),

            new NodeDef(
                AudioOutputTypeId, "Audio Output", "Output",
                [
                    Num("left", 0f, -1f, 1f),
                    Normalled("right", 0, -1f, 1f),
                    Num("gain", 0.5f, 0f, 1f),
                    Num("scan", 0f, 0f, 1f),
                    Num("scan rate", 60f, 1f, 2000f),
                ],
                [],
                (em, i) => [em.Mul(i[0], i[2]), em.Mul(i[1], i[2])],
                "The speakers. Leave 'right' unpatched and it carries 'left' through, "
                + "as a normalled jack would. 'scan' at 0 drives the patch from Time; at 1 "
                + "it sweeps the image and you hear the picture, at 'scan rate' sweeps per second."),

            new NodeDef(
                "audio.frequency", "Frequency", "Output",
                [Num("hz", 220f, 20f, 4000f)], [Num("out")],
                (_, i) => [i[0]],
                "A knob in hertz rather than in the single digits the visual modules use. "
                + "Patch it into an oscillator's freq to work at audible pitches."),

            new NodeDef(
                "audio.note", "Note", "Output",
                [
                    Pitched("note", 57f),
                    Num("octave", 0f, -4f, 4f),
                    Num("cents", 0f, -100f, 100f),
                ],
                [Num("hz"), Num("note")],
                (em, i) =>
                {
                    // Octaves are twelve semitones, so anything patched into
                    // 'octave' arrives on the same scale the note number is in
                    // and the two simply add up before the snap.
                    var wanted = em.Add(i[0], em.Mul(i[1], Pitch.Semitones));

                    // Halfway between two notes is where the snap belongs, and a
                    // floor of the note plus a half is that. Instant, with
                    // nothing smoothing it: a quantiser that eased into its notes
                    // would not be one. The click this once cost never came from
                    // here — an accumulated phase (ADR-0030) takes a frequency
                    // this steps and moves the waveform's slope rather than its
                    // value, and a slope has no click in it.
                    var note = em.Unary(OpCode.Floor, em.Add(wanted, 0.5f));

                    // Detune is applied after the snap, which is the whole point
                    // of having it: it is the one way to sit between two notes.
                    var tuned = em.Add(note, em.Mul(i[2], 0.01f));
                    var octaves = em.Mul(em.Add(tuned, -Pitch.ConcertNote), 1f / Pitch.Semitones);

                    return
                    [
                        em.Mul(em.Binary(OpCode.Pow, em.Constant(2f), octaves), Pitch.ConcertPitch),
                        note,
                    ];
                },
                "Frequency, but in notes. Pick one on the knob — 57 is A3 — or patch a signal "
                + "in and it snaps to the nearest whole note on its way through, which is what "
                + "turns a sweep into a run up the chromatic scale. The change is instant and "
                + "silent: the oscillators carry their phase, so a pitch that steps bends the "
                + "waveform rather than breaking it. 'hz' goes to an oscillator's freq; 'note' "
                + "hands the snapped number on, so a second Note can play an interval off it."),

            // ---------------------------------------------------------------- sources
            new NodeDef(
                "coord", "Coordinates", "Source",
                [], [Num("x"), Num("y"), Num("radius"), Num("angle")],
                (em, _) =>
                {
                    var x = em.Load(OpCode.LoadX);
                    var y = em.Load(OpCode.LoadY);
                    return
                    [
                        x,
                        y,
                        em.Binary(OpCode.Hypot, x, y),
                        em.Binary(OpCode.Atan2, y, x),
                    ];
                },
                "Where you are on screen. y runs -1..1, x is widened by the aspect ratio."),

            new NodeDef(
                "time", "Time", "Source",
                [Num("rate", 1f, 0f, 8f)], [Num("t")],
                (em, i) => [em.Mul(em.Load(OpCode.LoadT), i[0])],
                "Seconds since the patch started, scaled by rate."),

            new NodeDef(
                "value", "Value", "Source",
                [Num("value", 0.5f)], [Num("out")],
                (_, i) => [i[0]],
                "A knob. Handy when several modules should share one number."),

            // ---------------------------------------------------------------- oscillators
            Oscillator("osc.sine", "Sine", (em, p) => em.Unary(OpCode.Sin, em.Mul(p, Tau)),
                "The basic waveform. Smooth bands and blobs."),

            Oscillator("osc.saw", "Saw", (em, p) => em.Add(em.Mul(em.Unary(OpCode.Fract, p), 2f), -1f),
                "Ramps up then snaps back. Hard edges, good for stripes."),

            Oscillator("osc.triangle", "Triangle",
                (em, p) => em.Add(em.Mul(em.Unary(OpCode.Abs, em.Add(em.Unary(OpCode.Fract, p), -0.5f)), 4f), -1f),
                "Linear up and down. Softer than saw, sharper than sine."),

            Oscillator("osc.square", "Square",
                (em, p) => em.Add(em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), em.Unary(OpCode.Fract, p)), 2f), -1f),
                "Two values, nothing between. Pure hard-edged bands."),

            new NodeDef(
                "osc.pulse", "Pulse", "Oscillator",
                [Num("in"), Num("freq", 1f, 0f, 16f), Num("phase", 0f, 0f, 1f), Num("width", 0.5f, 0f, 1f), Num("amp", 1f, 0f, 2f), Num("bias", 0f, -2f, 2f)],
                [Num("out")],
                (em, i) =>
                {
                    var phase = em.Phase(i[0], i[1], i[2]);
                    var wave = em.Add(em.Mul(em.Binary(OpCode.Step, i[3], em.Unary(OpCode.Fract, phase)), 2f), -1f);
                    return [em.Add(em.Mul(wave, i[4]), i[5])];
                },
                "A square with an adjustable duty cycle."),

            // ---------------------------------------------------------------- sequencers
            StepSequencer(
                "seq.notes", "Note Sequencer",
                s => Pitched($"step {s + 1}", DefaultRiff[s]),
                "Eight notes in a row. A step is a note on a knob — 57 reads as A3 — and 'on' "
                + "silences one without losing it, so it doubles as that step's level. Send 'out' "
                + "to a Note and 'gate' to something that multiplies the tone, so a rest is heard "
                + "as one. 'shape' is how long the gate takes to open and close, as a fraction of a "
                + "step: turn it up for a swell, and it never quite reaches nothing, because a gate "
                + "that switched outright would click. 'index' is how far through the pattern the "
                + "sequence has got, which is the output to reach for on the screen."),

            StepSequencer(
                "seq.values", "Sequencer",
                s => Num($"step {s + 1}", DefaultShape[s], 0f, 1f),
                "The same eight steps as a plain signal rather than as notes — a hue, a scale, a "
                + "threshold, or a pitch by way of a Frequency. 'in' is a domain the way an "
                + "oscillator's is: stop it and the sequence stops, run it backwards and it runs "
                + "backwards, run it twice as fast and so does the pattern."),

            // ---------------------------------------------------------------- maths
            Binary("math.add", "Add", OpCode.Add, 0f, "a + b"),
            Binary("math.sub", "Subtract", OpCode.Sub, 0f, "a - b"),
            Binary("math.mul", "Multiply", OpCode.Mul, 1f, "a * b"),
            Binary("math.div", "Divide", OpCode.Div, 1f, "a / b, and 0 when b is 0."),
            Binary("math.mod", "Modulo", OpCode.Mod, 1f, "Remainder of a / b. Wraps values into a band."),
            Binary("math.pow", "Power", OpCode.Pow, 2f, "a raised to b."),
            Binary("math.min", "Minimum", OpCode.Min, 0f, "Whichever of a and b is smaller."),
            Binary("math.max", "Maximum", OpCode.Max, 0f, "Whichever of a and b is larger."),
            Binary("math.atan2", "Atan2", OpCode.Atan2, 1f, "Angle of the vector (b, a)."),
            Binary("math.hypot", "Length", OpCode.Hypot, 0f, "Distance from the origin to (a, b)."),

            Unary("math.abs", "Absolute", OpCode.Abs, "Drops the sign. Mirrors a signal about zero."),
            Unary("math.neg", "Negate", OpCode.Neg, "Flips the sign."),
            Unary("math.sin", "Sin", OpCode.Sin, "Sine, in radians."),
            Unary("math.cos", "Cos", OpCode.Cos, "Cosine, in radians."),
            Unary("math.tan", "Tan", OpCode.Tan, "Tangent, in radians."),
            Unary("math.sqrt", "Square root", OpCode.Sqrt, "Square root, and 0 for negatives."),
            Unary("math.floor", "Floor", OpCode.Floor, "Rounds down. Quantises a smooth signal into steps."),
            Unary("math.fract", "Fraction", OpCode.Fract, "Just the part after the decimal point. Wraps to 0..1."),
            Unary("math.sign", "Sign", OpCode.Sign, "-1, 0 or 1."),
            Unary("math.exp", "Exp", OpCode.Exp, "e raised to the input."),
            Unary("math.log", "Log", OpCode.Log, "Natural log, and 0 for non-positive input."),

            new NodeDef(
                "math.clamp", "Clamp", "Maths",
                [Any("in"), Any("low", -1f), Any("high", 1f)], [Any("out")],
                (em, i) => [em.Ternary(OpCode.Clamp, i[0], i[1], i[2])],
                "Holds the signal inside a range."),

            new NodeDef(
                "math.mix", "Mix", "Maths",
                [Any("a"), Any("b", 1f), Any("t", 0.5f, 0f, 1f)], [Any("out")],
                (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
                "Blends from a to b as t goes 0 to 1."),

            new NodeDef(
                "math.smoothstep", "Smoothstep", "Maths",
                [Any("edge0"), Any("edge1", 1f), Any("in")], [Any("out")],
                (em, i) => [em.Ternary(OpCode.Smoothstep, i[0], i[1], i[2])],
                "A soft 0-to-1 ramp between the two edges. The anti-aliased threshold."),

            new NodeDef(
                "math.step", "Threshold", "Maths",
                [Any("edge"), Any("in")], [Any("out")],
                (em, i) => [em.Binary(OpCode.Step, i[0], i[1])],
                "0 below the edge, 1 above it. A hard threshold."),

            new NodeDef(
                "math.remap", "Remap", "Maths",
                [Any("in"), Num("in low", -1f), Num("in high", 1f), Num("out low", 0f), Num("out high", 1f)],
                [Any("out")],
                (em, i) =>
                {
                    var t = em.Binary(OpCode.Div, em.Binary(OpCode.Sub, i[0], i[1]), em.Binary(OpCode.Sub, i[2], i[1]));
                    return [em.Ternary(OpCode.Mix, i[3], i[4], t)];
                },
                "Rescales one range onto another. Bipolar -1..1 into 0..1 is the common one."),

            // ---------------------------------------------------------------- space
            new NodeDef(
                "space.rotate", "Rotate", "Space",
                [Num("x"), Num("y"), Num("angle", 0f, -Tau, Tau)], [Num("x"), Num("y")],
                (em, i) =>
                {
                    var cos = em.Unary(OpCode.Cos, i[2]);
                    var sin = em.Unary(OpCode.Sin, i[2]);
                    return
                    [
                        em.Binary(OpCode.Sub, em.Mul(i[0], cos), em.Mul(i[1], sin)),
                        em.Binary(OpCode.Add, em.Mul(i[0], sin), em.Mul(i[1], cos)),
                    ];
                },
                "Spins the coordinate system. Feed the angle from an oscillator to make it turn."),

            new NodeDef(
                "space.scale", "Scale", "Space",
                [Num("x"), Num("y"), Num("scale", 1f, 0f, 16f)], [Num("x"), Num("y")],
                (em, i) => [em.Mul(i[0], i[2]), em.Mul(i[1], i[2])],
                "Zooms the coordinate system. Larger scale packs more pattern in."),

            new NodeDef(
                "space.translate", "Translate", "Space",
                [Num("x"), Num("y"), Num("dx"), Num("dy")], [Num("x"), Num("y")],
                (em, i) => [em.Binary(OpCode.Sub, i[0], i[2]), em.Binary(OpCode.Sub, i[1], i[3])],
                "Slides the coordinate system, moving the pattern by (dx, dy)."),

            new NodeDef(
                "space.polar", "To polar", "Space",
                [Num("x"), Num("y")], [Num("radius"), Num("angle")],
                (em, i) => [em.Binary(OpCode.Hypot, i[0], i[1]), em.Binary(OpCode.Atan2, i[1], i[0])],
                "Cartesian to polar. Patterns built on radius and angle go circular."),

            new NodeDef(
                "space.tile", "Tile", "Space",
                [Num("x"), Num("y"), Num("tiles", 3f, 1f, 16f)], [Num("x"), Num("y")],
                (em, i) =>
                {
                    Slot Cell(Slot v) =>
                        em.Add(em.Mul(em.Unary(OpCode.Fract, em.Add(em.Mul(em.Mul(v, i[2]), 0.5f), 0.5f)), 2f), -1f);
                    return [Cell(i[0]), Cell(i[1])];
                },
                "Repeats the coordinate system into a grid of identical cells."),

            new NodeDef(
                "space.mirror", "Mirror", "Space",
                [Num("x"), Num("y")], [Num("x"), Num("y")],
                (em, i) => [em.Unary(OpCode.Abs, i[0]), em.Unary(OpCode.Abs, i[1])],
                "Folds each axis about zero, so one quadrant is reflected into all four."),

            new NodeDef(
                "space.kaleidoscope", "Kaleidoscope", "Space",
                [Num("x"), Num("y"), Num("segments", 6f, 1f, 24f)], [Num("x"), Num("y")],
                (em, i) =>
                {
                    var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                    var angle = em.Binary(OpCode.Atan2, i[1], i[0]);
                    var segment = em.Binary(OpCode.Div, em.Constant(Tau), i[2]);
                    var half = em.Mul(segment, 0.5f);
                    var folded = em.Unary(OpCode.Abs,
                        em.Binary(OpCode.Sub, em.Binary(OpCode.Mod, angle, segment), half));
                    return
                    [
                        em.Mul(em.Unary(OpCode.Cos, folded), radius),
                        em.Mul(em.Unary(OpCode.Sin, folded), radius),
                    ];
                },
                "Folds the plane into wedges around the centre."),

            new NodeDef(
                "space.warp", "Warp", "Space",
                [Num("x"), Num("y"), Num("by"), Num("amount", 0.5f)], [Num("x"), Num("y")],
                (em, i) =>
                {
                    var push = em.Mul(i[2], i[3]);
                    return
                    [
                        em.Binary(OpCode.Add, i[0], push),
                        em.Binary(OpCode.Add, i[1], em.Unary(OpCode.Sin, em.Mul(push, Tau))),
                    ];
                },
                "Displaces coordinates by another signal. This is where patches stop looking geometric."),

            // ---------------------------------------------------------------- patterns
            new NodeDef(
                "pattern.noise", "Noise", "Pattern",
                [Num("x"), Num("y"), Num("z"), Num("scale", 2f, 0f, 32f)], [Num("out")],
                (em, i) => [em.Ternary(OpCode.Noise3, em.Mul(i[0], i[3]), em.Mul(i[1], i[3]), i[2])],
                "Smooth random field in 0..1. Drive z from Time to make it boil."),

            new NodeDef(
                "pattern.checker", "Checker", "Pattern",
                [Num("x"), Num("y"), Num("size", 4f, 0f, 32f)], [Num("out")],
                (em, i) =>
                {
                    var fx = em.Unary(OpCode.Floor, em.Mul(i[0], i[2]));
                    var fy = em.Unary(OpCode.Floor, em.Mul(i[1], i[2]));
                    return [em.Mul(em.Unary(OpCode.Fract, em.Mul(em.Add(fx, fy), 0.5f)), 2f)];
                },
                "A chequerboard, 0 or 1."),

            new NodeDef(
                "pattern.rings", "Rings", "Pattern",
                [Num("x"), Num("y"), Num("freq", 4f, 0f, 32f), Num("offset")], [Num("out")],
                (em, i) =>
                {
                    var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                    return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
                },
                "Concentric sine rings. Drive offset from Time to pulse outward."),

            // ---------------------------------------------------------------- colour
            new NodeDef(
                "colour.rgb", "RGB", "Colour",
                [Num("r", 0f, 0f, 1f), Num("g", 0f, 0f, 1f), Num("b", 0f, 0f, 1f)], [Col("colour")],
                (em, i) => [em.Combine(i[0], i[1], i[2])],
                "Builds a colour from three separate signals."),

            new NodeDef(
                "colour.hsv", "HSV", "Colour",
                [Num("hue", 0f, 0f, 1f), Num("saturation", 1f, 0f, 1f), Num("value", 1f, 0f, 1f)], [Col("colour")],
                (em, i) => [em.Triple(OpCode.HsvToRgb, i[0], i[1], i[2])],
                "Hue, saturation, value. Sweeping hue is the fastest route to rainbows."),

            new NodeDef(
                "colour.split", "Split", "Colour",
                [Col("colour")], [Num("r"), Num("g"), Num("b")],
                (_, i) => [Slot.Scalar(i[0].Base), Slot.Scalar(i[0].Base + 1), Slot.Scalar(i[0].Base + 2)],
                "Pulls a colour apart into its three channels."),

            new NodeDef(
                "colour.mix", "Blend", "Colour",
                [Col("a"), Col("b"), Num("t", 0.5f, 0f, 1f)], [Col("colour")],
                (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
                "Crossfades between two colours."),

            new NodeDef(
                "colour.gain", "Gain", "Colour",
                [Col("colour"), Any("gain", 1f, 0f, 4f), Any("bias", 0f, -1f, 1f)], [Col("colour")],
                (em, i) => [em.Binary(OpCode.Add, em.Binary(OpCode.Mul, i[0], i[1]), i[2])],
                "Brightness and contrast, as multiply then add."),

            // ---------------------------------------------------------------- feedback
            new NodeDef(
                "feedback", "Feedback", "Feedback",
                [Num("x"), Num("y")], [Col("colour")],
                (em, i) => [em.Triple(OpCode.SampleFeedback, i[0], i[1])],
                "Samples the previous frame. Wire the output back towards Output through "
                + "a Rotate or Scale to get the classic camera-pointed-at-its-own-monitor loop."),
        ];

        BuiltIn = ModuleCatalog.Of(BuiltInProvider, modules);
        Current = BuiltIn;
    }

    /// <summary>
    /// Eight steps is what fits on a node without the inspector turning into a
    /// list to scroll, and two chained through one another is sixteen.
    /// </summary>
    private const int SequencerSteps = 8;

    /// <summary>A minor pentatonic, so a Note Sequencer plays a tune the moment it is dropped.</summary>
    private static readonly float[] DefaultRiff = [57f, 60f, 62f, 64f, 67f, 64f, 62f, 60f];

    /// <summary>Up and back down — a shape, rather than the ramp 'index' already hands out.</summary>
    private static readonly float[] DefaultShape = [0f, 0.25f, 0.5f, 0.75f, 1f, 0.75f, 0.5f, 0.25f];

    /// <summary>
    /// The shortest the gate's edges may be made, as a fraction of a step. A
    /// knob turned to nothing would otherwise put the click back, and a gate
    /// that clicks is not one — so the knob shapes the edge and this decides
    /// that there is one. Two thousandths of a step is well under a millisecond
    /// at any tempo, which is a hard attack rather than a discontinuity.
    /// </summary>
    private const float ShortestGateEdge = 0.002f;

    /// <summary>
    /// Builds one of the two step sequencers. They differ only in what a step's
    /// knob means — a note number or an ordinary signal — because nothing below
    /// this line can tell the difference: the same stepped value is a melody at
    /// the speakers and a change of colour on the screen
    /// ([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)).
    /// </summary>
    /// <param name="step">The value knob for step <paramref name="step"/>'s index, zero-based.</param>
    private static NodeDef StepSequencer(
        string id, string name, Func<int, PortSpec> step, string description) => new(
        id, name, "Sequencer",
        [
            Num("in"),
            Num("rate", 4f, 0f, 32f),
            Num("steps", SequencerSteps, 1f, SequencerSteps),
            Num("gate length", 0.5f, 0f, 1f),
            Num("shape", 0.02f, 0f, 0.5f),
            .. Enumerable.Range(0, SequencerSteps)
                .SelectMany(s => new[] { step(s), Num($"on {s + 1}", 1f, 0f, 1f) }),
        ],
        [Num("out"), Num("gate"), Num("index")],
        EmitSequence,
        description);

    /// <summary>
    /// Which step the sequence is on is a function of where its input has got
    /// to, not of what it played before, so a sequencer needs no state at all —
    /// unlike a delay ([0027](0027-delay-lines-give-the-audio-path-a-memory.md))
    /// or an accumulated phase ([0030](0030-oscillators-accumulate-their-phase.md)).
    /// It draws the same thing it plays, and the video path pays nothing for it.
    /// </summary>
    /// <remarks>
    /// Selection is a sum of windows rather than a branch, because the machine
    /// has no branches. Each window is the difference between two thresholds on
    /// the step index, so adjacent windows share an edge and exactly one is ever
    /// open — which makes the sum the selected step and nothing else.
    /// </remarks>
    private static Slot[] EmitSequence(Emitter em, Slot[] i)
    {
        var count = em.Unary(OpCode.Floor,
            em.Ternary(OpCode.Clamp, i[2], em.Constant(1f), em.Constant(SequencerSteps)));

        // How far the input has travelled, counted in steps. Modulo is floored,
        // so an input running backwards runs the sequence backwards rather than
        // falling off the front of it.
        var travelled = em.Mul(i[0], i[1]);
        var index = em.Unary(OpCode.Floor, em.Binary(OpCode.Mod, travelled, count));

        var edges = new Slot[SequencerSteps + 1];
        edges[0] = em.Constant(1f);
        edges[SequencerSteps] = em.Constant(0f);

        for (var s = 1; s < SequencerSteps; s++)
            edges[s] = em.Binary(OpCode.Step, em.Constant(s), index);

        Slot value = default;
        Slot open = default;

        for (var s = 0; s < SequencerSteps; s++)
        {
            var window = em.Sub(edges[s], edges[s + 1]);
            var here = em.Mul(i[5 + s * 2], window);
            var sounds = em.Mul(i[6 + s * 2], window);

            (value, open) = s == 0 ? (here, sounds) : (em.Add(value, here), em.Add(open, sounds));
        }

        // The gate shuts partway through each step, so two identical notes in a
        // row are two notes rather than one held twice as long.
        //
        // It ramps rather than switches, and that is not a nicety. A switched
        // gate steps the amplitude by its whole height in one sample, which is a
        // discontinuity — and oversampling
        // ([0023](0023-oversample-the-audio-path.md)) band-limits one of those
        // rather than removing it, so it is heard as a click at every note.
        // Ramping also hides the other edge in here: because the envelope is
        // zero at each boundary, a step whose 'on' differs from the last one's
        // fades in at its own level instead of jumping to it.
        var within = em.Unary(OpCode.Fract, travelled);
        var shape = em.Ternary(OpCode.Clamp, i[4], em.Constant(ShortestGateEdge), em.Constant(1f));
        var length = em.Ternary(OpCode.Clamp, i[3], em.Constant(0f), em.Constant(1f));

        var opening = em.Ternary(OpCode.Smoothstep, em.Constant(0f), shape, within);
        var closing = em.Sub(em.Constant(1f),
            em.Ternary(OpCode.Smoothstep, em.Sub(length, shape), length, within));

        return
        [
            value,
            em.Mul(em.Mul(open, opening), closing),
            em.Binary(OpCode.Div, index, count),
        ];
    }

    /// <summary>
    /// Builds one of the fixed-shape oscillator modules. They share a socket
    /// layout and differ only in the waveform applied to the running phase.
    /// </summary>
    /// <remarks>
    /// The phase is accumulated rather than multiplied out, which is the whole
    /// of why a stepped pitch is silent on the audio path — see
    /// <see cref="OpCode.Phase"/>. Drawn rather than heard it is the multiply it
    /// always was, so the picture an oscillator makes is unchanged.
    /// </remarks>
    private static NodeDef Oscillator(
        string id, string name, Func<Emitter, Slot, Slot> waveform, string description) => new(
        id, name, "Oscillator",
        [
            Num("in"),
            Num("freq", 1f, 0f, 16f),
            Num("phase", 0f, 0f, 1f),
            Num("amp", 1f, 0f, 2f),
            Num("bias", 0f, -2f, 2f),
        ],
        [Num("out")],
        (em, i) =>
        {
            var phase = em.Phase(i[0], i[1], i[2]);
            return [em.Add(em.Mul(waveform(em, phase), i[3]), i[4])];
        },
        description);
}
