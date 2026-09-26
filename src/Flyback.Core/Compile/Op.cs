using System.Diagnostics.CodeAnalysis;

namespace Flyback.Core.Compile;

/// <summary>A single instruction: <c>reg[Out] = Code(reg[A], reg[B], reg[C], K)</c>.</summary>
[SuppressMessage("Design", "CA1051", Justification = "Plugin contract, and read per op in the interpreter's loop.")]
internal readonly struct Op(OpCode code, int outReg, int a = -1, int b = -1, int c = -1, float k = 0f)
{
    public readonly OpCode Code = code;
    public readonly int Out = outReg;
    public readonly int A = a;
    public readonly int B = b;
    public readonly int C = c;
    public readonly float K = k;

    public override string ToString() => $"r{Out} = {Code}(r{A}, r{B}, r{C}, {K})";
}