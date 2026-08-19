using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Maths()
    {
        yield return Binary("math.add", "Add", OpCode.Add, 0f, "a + b");
        yield return Binary("math.sub", "Subtract", OpCode.Sub, 0f, "a - b");
        yield return Binary("math.mul", "Multiply", OpCode.Mul, 1f, "a * b");
        yield return Binary("math.div", "Divide", OpCode.Div, 1f, "a / b, and 0 when b is 0.");
        yield return Binary("math.mod", "Modulo", OpCode.Mod, 1f, "Remainder of a / b. Wraps values into a band.");
        yield return Binary("math.pow", "Power", OpCode.Pow, 2f, "a raised to b.");
        yield return Binary("math.min", "Minimum", OpCode.Min, 0f, "Whichever of a and b is smaller.");
        yield return Binary("math.max", "Maximum", OpCode.Max, 0f, "Whichever of a and b is larger.");
        yield return Binary("math.atan2", "Atan2", OpCode.Atan2, 1f, "Angle of the vector (b, a).");
        yield return Binary("math.hypot", "Length", OpCode.Hypot, 0f, "Distance from the origin to (a, b).");

        yield return Unary("math.abs", "Absolute", OpCode.Abs, "Drops the sign. Mirrors a signal about zero.");
        yield return Unary("math.neg", "Negate", OpCode.Neg, "Flips the sign.");
        yield return Unary("math.sin", "Sin", OpCode.Sin, "Sine, in radians.");
        yield return Unary("math.cos", "Cos", OpCode.Cos, "Cosine, in radians.");
        yield return Unary("math.tan", "Tan", OpCode.Tan, "Tangent, in radians.");
        yield return Unary("math.sqrt", "Square root", OpCode.Sqrt, "Square root, and 0 for negatives.");
        yield return Unary("math.floor", "Floor", OpCode.Floor, "Rounds down. Quantises a smooth signal into steps.");
        yield return Unary("math.fract", "Fraction", OpCode.Fract, "Just the part after the decimal point. Wraps to 0..1.");
        yield return Unary("math.sign", "Sign", OpCode.Sign, "-1, 0 or 1.");
        yield return Unary("math.exp", "Exp", OpCode.Exp, "e raised to the input.");
        yield return Unary("math.log", "Log", OpCode.Log, "Natural log, and 0 for non-positive input.");

        yield return new NodeDef(
            "math.clamp", "Clamp", "Maths",
            [Any("in"), Any("low", -1f), Any("high", 1f)], [Any("out")],
            (em, i) => [em.Ternary(OpCode.Clamp, i[0], i[1], i[2])],
            "Holds the signal inside a range.");

        yield return new NodeDef(
            "math.mix", "Mix", "Maths",
            [Any("a"), Any("b", 1f), Any("t", 0.5f, 0f, 1f)], [Any("out")],
            (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
            "Blends from a to b as t goes 0 to 1.");

        yield return Mixer();

        yield return new NodeDef(
            "math.smoothstep", "Smoothstep", "Maths",
            [Any("edge0"), Any("edge1", 1f), Any("in")], [Any("out")],
            (em, i) => [em.Ternary(OpCode.Smoothstep, i[0], i[1], i[2])],
            "A soft 0-to-1 ramp between the two edges. The anti-aliased threshold.");

        yield return new NodeDef(
            "math.step", "Threshold", "Maths",
            [Any("edge"), Any("in")], [Any("out")],
            (em, i) => [em.Binary(OpCode.Step, i[0], i[1])],
            "0 below the edge, 1 above it. A hard threshold.");

        yield return new NodeDef(
            "math.remap", "Remap", "Maths",
            [Any("in"), Num("in low", -1f), Num("in high", 1f), Num("out low"), Num("out high", 1f)],
            [Any("out")],
            (em, i) =>
            {
                var t = em.Binary(OpCode.Div, em.Binary(OpCode.Sub, i[0], i[1]), em.Binary(OpCode.Sub, i[2], i[1]));
                return [em.Ternary(OpCode.Mix, i[3], i[4], t)];
            },
            "Rescales one range onto another. Bipolar -1..1 into 0..1 is the common one.");
    }

    private static NodeDef Unary(string id, string name, OpCode code, string description) => new(
        id, name, "Maths", [Any("in")], [Any("out")],
        (em, i) => [em.Unary(code, i[0])], description);

    private static NodeDef Binary(
        string id, string name, OpCode code, float defaultB, string description) => new(
        id, name, "Maths", [Any("a"), Any("b", defaultB)], [Any("out")],
        (em, i) => [em.Binary(code, i[0], i[1])], description);
    
    /// <summary>
    /// Four inputs, a level on each, summed into one — the desk, rather than
    /// four Multiplies wired into a chain of Adds.
    /// </summary>
    /// <remarks>
    /// Every socket is an <see cref="PortKind.Any"/>, so this is one module for
    /// both halves of the machine: four tones sum to a chord and four fields sum
    /// to an image, by the same ops. A level is a socket like any other besides,
    /// which is what makes a fader something an oscillator can sweep rather than
    /// only something a hand can set.
    /// </remarks>
    private static NodeDef Mixer()
    {
        const int channels = 4;

        var ports = new PortSpec[channels * 2];
        for (var ch = 0; ch < channels; ch++)
        {
            ports[ch * 2] = Any($"in {ch + 1}");
            ports[ch * 2 + 1] = Any($"level {ch + 1}", 1f, 0f, 1f);
        }

        return new NodeDef(
            "math.mixer", "Mixer", "Maths",
            ports, [Any("out")],
            (em, i) =>
            {
                var sum = em.Mul(i[0], i[1]);
                for (var ch = 1; ch < channels; ch++)
                    sum = em.Add(sum, em.Mul(i[ch * 2], i[ch * 2 + 1]));
                return [sum];
            },
            "Four signals summed, each through its own level. It sums the way a desk does "
            + "rather than averaging, so four things at full is four times as loud — pull the "
            + "levels down, or the Output's gain. An unused input rests on a knob at zero, so it "
            + "adds nothing until something is patched in. Colours mix as readily as tones: patch "
            + "a picture into any input and the levels are a four-way blend of pictures.");
    }
}