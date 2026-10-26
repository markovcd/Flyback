using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// A sound's ops split in two for the interpreter: those that follow from the program's
/// constants alone, run once a buffer, and the rest, run every sample in their order.
/// </summary>
/// <remarks>
/// A fixed op is a <see cref="OpCode.Const"/>, or an op with no memory and nothing read
/// from outside whose every input is fixed. Live values stay per sample: a key or a
/// controller is written from another thread and may change inside a buffer. The varying
/// ops keep their order, every stateful one among them, so a delay line or a phase cell is
/// still the one its position names, and the two runs together do what one whole run does.
/// About a fifth of a large sound's ops are fixed.
/// </remarks>
public sealed class SoundPlan
{
    private SoundPlan(Op[] @fixed, Op[] varying)
    {
        Fixed = @fixed;
        Varying = varying;
    }

    /// <summary>The ops that follow from the constants alone, in program order.</summary>
    internal Op[] Fixed { get; }

    /// <summary>Every other op, in program order.</summary>
    internal Op[] Varying { get; }

    /// <summary>
    /// Splits <paramref name="ops"/>, or answers null where nothing is fixed or a register
    /// is written twice, as <see cref="FramePlan.For"/> refuses; such a program runs whole.
    /// </summary>
    internal static SoundPlan? For(Op[] ops, int registerCount)
    {
        ArgumentNullException.ThrowIfNull(ops);

        var known = new bool[registerCount];
        var written = new bool[registerCount];
        var isFixed = new bool[ops.Length];
        var count = 0;

        for (var i = 0; i < ops.Length; i++)
        {
            var op = ops[i];

            isFixed[i] = op.Code is OpCode.Const || (Pure(op.Code) && OpShape.Reads(op).All(register => known[register]));

            for (var w = 0; w < OpShape.Outputs(op.Code); w++)
            {
                if (written[op.Out + w]) return null;

                written[op.Out + w] = true;
                known[op.Out + w] = isFixed[i];
            }

            if (isFixed[i]) count++;
        }

        if (count == 0) return null;

        return new SoundPlan(
            [.. ops.Where((_, i) => isFixed[i])],
            [.. ops.Where((_, i) => !isFixed[i])]);
    }

    /// <summary>
    /// An op whose result is its inputs' alone: no memory of its own, and nothing read from
    /// the clock, a coordinate, the player, a table or a picture.
    /// </summary>
    private static bool Pure(OpCode code) =>
        OpShape.Outputs(code) > 0
        && OpShape.Owns(code) == OpMemory.None
        && code is not (OpCode.LoadX or OpCode.LoadY or OpCode.LoadT or OpCode.LoadAspect or OpCode.LoadFeedbackAge
            or OpCode.LoadLive or OpCode.UnitRead or OpCode.PlaneRead or OpCode.SampleFeedback or OpCode.SamplePicture
            or OpCode.Table);
}
