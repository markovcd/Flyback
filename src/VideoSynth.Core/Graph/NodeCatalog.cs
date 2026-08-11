using VideoSynth.Core.Compile;

namespace VideoSynth.Core.Graph;

/// <summary>
/// Every module the synth knows how to build. Each entry pairs a socket layout
/// with the ops it lowers to; adding a module here makes it appear in the
/// editor palette and compile with no other changes.
/// </summary>
public static class NodeCatalog
{
    public const string OutputTypeId = "output";

    private const float Tau = 6.283185307179586f;

    private static readonly Dictionary<string, NodeDef> Index;

    public static IReadOnlyList<NodeDef> All { get; }

    public static IEnumerable<string> Categories => All.Select(d => d.Category).Distinct();

    public static NodeDef? Get(string typeId) => Index.GetValueOrDefault(typeId);

    public static NodeDef Require(string typeId) =>
        Get(typeId) ?? throw new KeyNotFoundException($"Unknown node type '{typeId}'.");

    // --- port shorthands -----------------------------------------------------

    private static PortSpec Num(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, value, min, max);

    private static PortSpec Col(string name) => new(name, PortKind.Colour);

    private static PortSpec Any(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Any, value, min, max);

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
        All =
        [
            // ---------------------------------------------------------------- output
            new NodeDef(
                OutputTypeId, "Output", "Output",
                [Col("colour")], [],
                (_, i) => [i[0]],
                "The screen. Everything upstream of this is what you see."),

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
                    var phase = em.Add(em.Mul(i[0], i[1]), i[2]);
                    var wave = em.Add(em.Mul(em.Binary(OpCode.Step, i[3], em.Unary(OpCode.Fract, phase)), 2f), -1f);
                    return [em.Add(em.Mul(wave, i[4]), i[5])];
                },
                "A square with an adjustable duty cycle."),

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
                [Any("edge0", 0f), Any("edge1", 1f), Any("in")], [Any("out")],
                (em, i) => [em.Ternary(OpCode.Smoothstep, i[0], i[1], i[2])],
                "A soft 0-to-1 ramp between the two edges. The anti-aliased threshold."),

            new NodeDef(
                "math.step", "Threshold", "Maths",
                [Any("edge", 0f), Any("in")], [Any("out")],
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
                [Num("x"), Num("y"), Num("by"), Num("amount", 0.5f, -4f, 4f)], [Num("x"), Num("y")],
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

        Index = All.ToDictionary(d => d.TypeId);
    }

    /// <summary>
    /// Builds one of the fixed-shape oscillator modules. They share a socket
    /// layout and differ only in the waveform applied to the running phase.
    /// </summary>
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
            var phase = em.Add(em.Mul(i[0], i[1]), i[2]);
            return [em.Add(em.Mul(waveform(em, phase), i[3]), i[4])];
        },
        description);
}
