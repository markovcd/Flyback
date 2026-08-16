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

    // --- stateful: these remember something from the last evaluation, and are
    //     the only ops that do. The video path renders pixels in parallel and
    //     out of order, so it passes no state and each of them falls back to
    //     something total there rather than refusing to compile.

    /// <summary>
    /// out = line[now - c seconds], then line writes a + clamp(b) * out.
    /// A feedback comb: the delay itself, and the building block of a reverb.
    /// K is the longest delay this instance will ever be asked for, which is what
    /// the buffer is sized from.
    /// </summary>
    Delay,

    /// <summary>
    /// out = line[now - c] - b * v, where v = a + b * line[now - c] is what gets
    /// written. A Schroeder allpass: it smears a signal in time without colouring
    /// it, which is what turns a bank of combs into a reverb rather than an echo.
    /// </summary>
    Allpass,

    /// <summary>
    /// out = fract(phase + (a - a_previous) * b) + c, where phase is carried
    /// from the last evaluation. A phase accumulator: the running total of how
    /// far the domain 'a' has moved, counted in cycles of 'b' as it was at each
    /// step, with 'c' added on afterwards rather than integrated.
    /// <para>
    /// Integrating is what makes a frequency change silent. Multiplying instead
    /// — phase = a * b, which is what this path did before it had any state —
    /// makes phase jump by a times the change in b whenever b moves, so a
    /// stepped pitch tears the waveform by more the longer the patch has run.
    /// The accumulated phase moves by one step's worth however far b jumps, so
    /// the wave's value stays continuous and only its slope changes.
    /// </para>
    /// <para>
    /// Without state it falls back to a * b + c, which is exactly the old
    /// multiply. A picture is one evaluation per pixel with no previous sample
    /// to carry anything from, and there the two agree.
    /// </para>
    /// </summary>
    Phase,

    // --- multi-register writes: these fill out, out+1, out+2 ---
    /// <summary>(out, out+1, out+2) = hsv2rgb(a, b, c)</summary>
    HsvToRgb,

    /// <summary>(out, out+1, out+2) = previous frame sampled at (a, b)</summary>
    SampleFeedback,
}
