namespace Flyback.Core.Compile;

/// <summary>
/// A compiled port value: <see cref="Width"/> consecutive registers starting at
/// <see cref="Base"/>. Width is 1 for a scalar signal and 3 for a color.
/// </summary>
public readonly record struct Slot(int Base, int Width)
{
    /// <summary>A scalar signal held in <paramref name="register"/>.</summary>
    public static Slot Scalar(int register) => new(register, 1);

    /// <summary>A color held in three registers from <paramref name="firstRegister"/>: red, green, blue.</summary>
    internal static Slot Color(int firstRegister) => new(firstRegister, 3);

    /// <summary>
    /// Register holding component <paramref name="i"/>, broadcasting a scalar
    /// across all components the way a shading language would.
    /// </summary>
    public int Component(int i) => Width == 1 ? Base : Base + i;
}
