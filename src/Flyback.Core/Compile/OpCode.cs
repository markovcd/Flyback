namespace Flyback.Core.Compile;

/// <summary>
/// Instruction set of the scalar register machine a patch compiles down to.
/// Every op reads from and writes to <c>float</c> registers, so the whole
/// program is a flat, allocation-free list that can be walked per pixel.
/// Keeping it flat (rather than walking the node objects) is also what makes a
/// GLSL backend straightforward later: each op maps to one line of shader code.
/// </summary>
public enum OpCode : byte
{
    /// <summary>out = K</summary>
    Const,

    /// <summary>out = pixel x coordinate</summary>
    LoadX,

    /// <summary>out = pixel y coordinate</summary>
    LoadY,

    /// <summary>out = current time in seconds</summary>
    LoadT,

    /// <summary>out = a</summary>
    Copy,

    // --- unary ---
    Neg,
    Abs,
    Sin,
    Cos,
    Tan,
    Sqrt,
    Floor,
    Ceil,
    Fract,
    Sign,
    Exp,
    Log,

    // --- binary ---
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Pow,
    Min,
    Max,
    Atan2,

    /// <summary>out = b &lt; a ? 0 : 1 (GLSL step(edge: a, x: b))</summary>
    Step,

    /// <summary>out = sqrt(a*a + b*b)</summary>
    Hypot,

    // --- ternary ---
    /// <summary>out = clamp(a, b, c)</summary>
    Clamp,

    /// <summary>out = a + (b - a) * c</summary>
    Mix,

    /// <summary>out = smoothstep(edge0: a, edge1: b, x: c)</summary>
    Smoothstep,

    /// <summary>out = value noise at (a, b, c)</summary>
    Noise3,

    // --- multi-register writes: these fill out, out+1, out+2 ---
    /// <summary>(out, out+1, out+2) = hsv2rgb(a, b, c)</summary>
    HsvToRgb,

    /// <summary>(out, out+1, out+2) = previous frame sampled at (a, b)</summary>
    SampleFeedback,
}
