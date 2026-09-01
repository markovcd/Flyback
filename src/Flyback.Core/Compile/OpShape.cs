namespace Flyback.Core.Compile;

/// <summary>
/// Which of an <see cref="Op"/>'s register fields each <see cref="OpCode"/>
/// actually touches: how many inputs it reads, and how wide a result it writes.
/// </summary>
/// <remarks>
/// The interpreter reads registers without a bounds check, so something has to
/// have established that every index an op names is one the register bank holds
/// — and that is <see cref="CompiledPatch"/>'s constructor, walking the program
/// once with this. It can only ask about the fields an op reads: <c>A</c> is -1
/// on a <see cref="OpCode.Const"/> and reading it would fail a check that the
/// interpreter never performs, so a table of arities is what separates "names a
/// register out of range" from "names no register at all".
/// <para>
/// A table rather than a property on the op, because the answer belongs to the
/// code and not to the instance — every Add reads two, and storing that on each
/// of them would be the same byte written a thousand times and a thousand
/// chances to write it wrong.
/// </para>
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
            or OpCode.LoadLive
            or OpCode.UnitRead => 0,

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

        _ => 3,
    };

    /// <summary>
    /// How many consecutive registers the op writes at <c>Out</c>, and zero for
    /// the three that write none.
    /// </summary>
    public static int Outputs(OpCode code) => code switch
    {
        OpCode.Tap or OpCode.UnitWrite or OpCode.ClockWrite => 0,
        OpCode.HsvToRgb or OpCode.SampleFeedback or OpCode.SamplePicture => 3,
        _ => 1,
    };
}
