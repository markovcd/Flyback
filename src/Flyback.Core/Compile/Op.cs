namespace Flyback.Core.Compile;

/// <summary>A single instruction: <c>reg[Out] = Code(reg[A], reg[B], reg[C], K)</c>.</summary>
public readonly struct Op(OpCode code, int outReg, int a = -1, int b = -1, int c = -1, float k = 0f)
{
    public readonly OpCode Code = code;
    public readonly int Out = outReg;
    public readonly int A = a;
    public readonly int B = b;
    public readonly int C = c;
    public readonly float K = k;

    public override string ToString() => $"r{Out} = {Code}(r{A}, r{B}, r{C}, {K})";
}

/// <summary>
/// A compiled port value: <see cref="Width"/> consecutive registers starting at
/// <see cref="Base"/>. Width is 1 for a scalar signal and 3 for a color.
/// </summary>
public readonly record struct Slot(int Base, int Width)
{
    public static Slot Scalar(int register) => new(register, 1);

    public static Slot Color(int firstRegister) => new(firstRegister, 3);

    /// <summary>
    /// Register holding component <paramref name="i"/>, broadcasting a scalar
    /// across all components the way a shading language would.
    /// </summary>
    public int Component(int i) => Width == 1 ? Base : Base + i;
}
