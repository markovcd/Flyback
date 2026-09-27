namespace Flyback.Core.Compile;

/// <summary>
/// A program with its constants left out: two patches of one shape run the same
/// machine code, which is what makes turning a knob free for the IL backend.
/// </summary>
/// <remarks>
/// Every other field of every op is part of the shape, <see cref="Op.K"/> included,
/// because outside a constant K is baked into the code — which delay line, which
/// live input, which clip. The frame plan is not, because it is worked out from
/// exactly the fields that are.
/// </remarks>
internal sealed class IlShape : IEquatable<IlShape>
{
    private readonly Op[] ops;
    private readonly int registerCount;
    private readonly int outputBase;
    private readonly int outputWidth;
    private readonly int hash;

    public IlShape(CompiledPatch patch)
    {
        ops = patch.Ops;
        registerCount = patch.RegisterCount;
        outputBase = patch.OutputBase;
        outputWidth = patch.OutputWidth;

        var code = new HashCode();
        code.Add(registerCount);
        code.Add(outputBase);
        code.Add(outputWidth);

        foreach (var op in ops)
        {
            code.Add(op.Code);
            code.Add(op.Out);
            code.Add(op.A);
            code.Add(op.B);
            code.Add(op.C);
            if (op.Code is not OpCode.Const) code.Add(BitConverter.SingleToInt32Bits(op.K));
        }

        hash = code.ToHashCode();
    }

    public bool Equals(IlShape? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (hash != other.hash
            || registerCount != other.registerCount
            || outputBase != other.outputBase
            || outputWidth != other.outputWidth
            || ops.Length != other.ops.Length)
            return false;

        for (var i = 0; i < ops.Length; i++)
        {
            Op a = ops[i], b = other.ops[i];

            if (a.Code != b.Code || a.Out != b.Out || a.A != b.A || a.B != b.B || a.C != b.C) return false;

            if (a.Code is not OpCode.Const
                && BitConverter.SingleToInt32Bits(a.K) != BitConverter.SingleToInt32Bits(b.K))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as IlShape);

    public override int GetHashCode() => hash;
}