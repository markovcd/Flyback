using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// The ops of a program that work out exactly what an earlier op worked out: the
/// same arithmetic on the same registers, which two modules reading one signal the
/// same way leave behind. The IL reads the earlier answer instead.
/// </summary>
/// <remarks>
/// Only arithmetic that holds no state and reads none: a delay, an accumulator or a
/// cell read gives a different answer each time it runs. A constant is left alone
/// because the code is shared with patches of the same shape, whose knobs differ.
/// A register written twice, and a program output, are never matched, so every
/// answer read in place of another is one the program never overwrote.
/// </remarks>
internal sealed class Repeats
{
    private readonly bool[] repeated;
    private readonly Dictionary<int, int> same = [];

    public Repeats(Op[] ops, (int From, int To) outputs)
    {
        repeated = new bool[ops.Length];

        var writes = new Dictionary<int, int>();
        foreach (var op in ops)
            for (var w = 0; w < OpShape.Outputs(op.Code); w++)
                writes[op.Out + w] = writes.GetValueOrDefault(op.Out + w) + 1;

        var first = new Dictionary<(OpCode, int, int, int, int), int>();

        for (var i = 0; i < ops.Length; i++)
        {
            var op = ops[i];
            if (!Pure(op.Code) || writes[op.Out] != 1 || (op.Out >= outputs.From && op.Out < outputs.To)) continue;

            var inputs = OpShape.Inputs(op.Code);
            var a = inputs > 0 ? Resolve(op.A) : -1;
            var b = inputs > 1 ? Resolve(op.B) : -1;
            var c = inputs > 2 ? Resolve(op.C) : -1;

            if ((a >= 0 && writes.GetValueOrDefault(a) != 1)
                || (b >= 0 && writes.GetValueOrDefault(b) != 1)
                || (c >= 0 && writes.GetValueOrDefault(c) != 1))
                continue;

            var key = (op.Code, a, b, c, BitConverter.SingleToInt32Bits(op.K));

            if (first.TryGetValue(key, out var earlier))
            {
                repeated[i] = true;
                same[op.Out] = earlier;
            }
            else
            {
                first[key] = op.Out;
            }
        }
    }

    /// <summary>Whether op <paramref name="op"/> repeats an earlier one, and so is not emitted.</summary>
    public bool Repeated(int op) => repeated[op];

    /// <summary>The register holding <paramref name="register"/>'s value: its own, or the earlier op's it repeats.</summary>
    public int Resolve(int register) => same.GetValueOrDefault(register, register);

    private static bool Pure(OpCode code) =>
        OpShape.Outputs(code) == 1
        && !OpShape.Kept(code)
        && code is not (OpCode.Const or OpCode.Copy or OpCode.UnitRead or OpCode.PlaneRead or OpCode.LoadLive
            or OpCode.LoadX or OpCode.LoadY or OpCode.LoadT or OpCode.LoadAspect or OpCode.LoadFeedbackAge);
}
