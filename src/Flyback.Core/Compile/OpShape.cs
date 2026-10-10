namespace Flyback.Core.Compile;

/// <summary>
/// Which of an <see cref="Op"/>'s register fields each <see cref="OpCode"/>
/// actually touches: how many inputs it reads, how wide a result it writes, and
/// what it owns by its position.
/// </summary>
/// <remarks>
/// The interpreter reads registers without a bounds check, so
/// <c>CompiledPatch</c>'s constructor walks the program once with this. It
/// can only ask about the fields an op reads — <c>A</c> is -1 on a
/// <see cref="OpCode.Const"/> — so a table of arities is what separates "names a
/// register out of range" from "names no register at all". A table rather than a
/// property on the op, because every Add reads two and storing that on each of
/// them would be a thousand chances to write it wrong. Every opcode is named here,
/// so one added without a shape is a build error, not a guess.
/// </remarks>
internal static class OpShape
{
    /// <summary>How many of <c>A</c>, <c>B</c> and <c>C</c> the op reads.</summary>
    public static int Inputs(OpCode code) => code switch
    {
        OpCode.Const
            or OpCode.LoadX
            or OpCode.LoadY
            or OpCode.LoadT
            or OpCode.LoadAspect
            or OpCode.LoadFeedbackAge
            or OpCode.LoadLive
            or OpCode.UnitRead
            or OpCode.PlaneRead => 0,

        OpCode.Copy
            or OpCode.Neg
            or OpCode.Abs
            or OpCode.Sin
            or OpCode.Cos
            or OpCode.Tan
            or OpCode.Sqrt
            or OpCode.Floor
            or OpCode.Ceil
            or OpCode.Fract
            or OpCode.Sign
            or OpCode.Exp
            or OpCode.Log
            or OpCode.Table
            or OpCode.Tap
            or OpCode.UnitWrite
            or OpCode.PlaneWrite
            or OpCode.ClockWrite => 1,

        OpCode.Add
            or OpCode.Sub
            or OpCode.Mul
            or OpCode.Div
            or OpCode.Mod
            or OpCode.Pow
            or OpCode.Min
            or OpCode.Max
            or OpCode.Atan2
            or OpCode.Step
            or OpCode.Hypot
            or OpCode.SampleFeedback
            or OpCode.SamplePicture => 2,

        OpCode.Clamp
            or OpCode.Mix
            or OpCode.Smoothstep
            or OpCode.Noise3
            or OpCode.Delay
            or OpCode.Allpass
            or OpCode.Phase
            or OpCode.HsvToRgb => 3,

        _ => throw Unshaped(code),
    };

    /// <summary>
    /// How many consecutive registers the op writes at <c>Out</c>, and zero for
    /// the four that write none.
    /// </summary>
    public static int Outputs(OpCode code) => code switch
    {
        OpCode.Tap or OpCode.UnitWrite or OpCode.PlaneWrite or OpCode.ClockWrite => 0,

        OpCode.HsvToRgb or OpCode.SampleFeedback or OpCode.SamplePicture => 3,

        OpCode.Const
            or OpCode.LoadX
            or OpCode.LoadY
            or OpCode.LoadT
            or OpCode.LoadAspect
            or OpCode.LoadFeedbackAge
            or OpCode.LoadLive
            or OpCode.Copy
            or OpCode.Neg
            or OpCode.Abs
            or OpCode.Sin
            or OpCode.Cos
            or OpCode.Tan
            or OpCode.Sqrt
            or OpCode.Floor
            or OpCode.Ceil
            or OpCode.Fract
            or OpCode.Sign
            or OpCode.Exp
            or OpCode.Log
            or OpCode.Add
            or OpCode.Sub
            or OpCode.Mul
            or OpCode.Div
            or OpCode.Mod
            or OpCode.Pow
            or OpCode.Min
            or OpCode.Max
            or OpCode.Atan2
            or OpCode.Step
            or OpCode.Hypot
            or OpCode.Clamp
            or OpCode.Mix
            or OpCode.Smoothstep
            or OpCode.Noise3
            or OpCode.Delay
            or OpCode.Allpass
            or OpCode.Phase
            or OpCode.UnitRead
            or OpCode.PlaneRead
            or OpCode.Table => 1,

        _ => throw Unshaped(code),
    };

    /// <summary>The registers the op reads: <c>A</c>, then <c>B</c>, then <c>C</c>, as many as it has inputs.</summary>
    public static IEnumerable<int> Reads(Op op)
    {
        var inputs = Inputs(op.Code);

        if (inputs > 0) yield return op.A;
        if (inputs > 1) yield return op.B;
        if (inputs > 2) yield return op.C;
    }

    /// <summary>What the op owns by its position in the program, which every backend counts the same way.</summary>
    public static OpMemory Owns(OpCode code) => code switch
    {
        OpCode.Delay or OpCode.Allpass => OpMemory.Line,
        OpCode.Phase => OpMemory.Cell,
        _ => OpMemory.None,
    };

    /// <summary>
    /// Whether the op stays in a program that never reads its result — see
    /// <see cref="Emitter.ToProgram(Slot)"/>.
    /// </summary>
    /// <remarks>
    /// An op that writes no register is kept because a register was never what it
    /// was for. One that owns memory by position is kept because the position is
    /// the memory: a renderer hands out delay lines and phase cells in the order
    /// these ops run, and <see cref="StateOwners"/> names their modules in the
    /// same order, so taking one out would hand every one after it its
    /// neighbor's past.
    /// </remarks>
    public static bool Kept(OpCode code) => Outputs(code) == 0 || Owns(code) != OpMemory.None;

    private static ArgumentOutOfRangeException Unshaped(OpCode code) =>
        new(nameof(code), code, "An opcode with no shape: name it in OpShape.");
}
