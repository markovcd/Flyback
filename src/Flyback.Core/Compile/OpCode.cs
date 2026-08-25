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
    /// written. A Schroeder allpass: it smears a signal in time without coloring
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

    /// <summary>
    /// out = the value slot K held when the previous evaluation finished, and
    /// zero before there has been one.
    /// </summary>
    /// <remarks>
    /// Half of a pair, and the half that stands where the graph wants a value.
    /// Its <see cref="UnitWrite"/> is emitted after every read in the program, so
    /// what a read hands back is always one evaluation old however the wires run.
    /// That gap is the whole point: it is what lets a patch hold a loop at all,
    /// and it is one evaluation for the same reason a rack of one-sample modules
    /// gives you one — there is nowhere shorter for a cycle to be.
    /// <para>
    /// Unlike the delay lines, K here is a slot number rather than a length. The
    /// read and the write are separate ops that must agree about which cell they
    /// mean, and counting positions the way <see cref="Delay"/> does would leave
    /// that agreement resting on emit order — which for these two, unlike every
    /// other stateful op, is deliberately not the same.
    /// </para>
    /// </remarks>
    UnitRead,

    /// <summary>
    /// slot K = a. The one op that writes no register at all, because what it
    /// writes is read by the next evaluation's <see cref="UnitRead"/> rather than
    /// by anything in this one. <c>Out</c> is -1 to say so.
    /// </summary>
    UnitWrite,

    /// <summary>
    /// slot K = a, unbounded. <see cref="UnitWrite"/> for a cell holding the
    /// renderer's clock rather than a signal from the patch.
    /// </summary>
    /// <remarks>
    /// The two differ only in the bound, and the bound is the whole point. A
    /// cell a patch can draw a wire into may be part of a loop with a gain above
    /// one, so what goes into it is clamped to the rails — which is what keeps a
    /// runaway audible instead of turning it into silent NaN. A clock is not
    /// that: no wire reaches it, it cannot run away, and it passes any bound
    /// simply by the patch being left playing. Clamped, it sticks, and every
    /// module that measures its own rate off it is handed a rate that grows
    /// without end.
    /// </remarks>
    ClockWrite,

    /// <summary>
    /// out = clip K at a seconds from its start, interpolated, and silence
    /// either side of it.
    /// </summary>
    /// <remarks>
    /// The one op that reads something the patch did not compute. K is which
    /// clip rather than how long a buffer is, and the audio behind it is carried
    /// by the program itself — see <see cref="CompiledPatch.Tables"/> — because
    /// it is the same for every evaluation and for every renderer.
    /// <para>
    /// Not stateful, despite sitting beside the ops that are: a clip is a
    /// function of the position asked for and of nothing that happened before.
    /// It is listed here because it is the other op whose answer comes from
    /// outside the register file, and because it shares their fallback — a
    /// program compiled with no clips reads silence, which is what the shader
    /// does and what the screen gets.
    /// </para>
    /// </remarks>
    Table,

    /// <summary>
    /// trace K keeps a, and nothing is written to a register.
    /// </summary>
    /// <remarks>
    /// The one op whose whole purpose is outside the program. Everything else
    /// here computes something the next op or the sink will read; this hands a
    /// value to whoever is watching and produces nothing. A Scope is the only
    /// module that emits one — see <see cref="DelayState.Tap"/>.
    /// <para>
    /// It is also the one op that makes a program larger than what it computes.
    /// A Scope is not reachable from the speakers, so the audio walk would never
    /// visit what it is looking at; the compiler roots at every tap as well as
    /// at the sink, which keeps its input alive on a path that has no other use
    /// for it. That is exactly the dead-code elimination of ADR-0022 being given
    /// up on purpose, for the one thing that cannot work without it.
    /// </para>
    /// </remarks>
    Tap,

    // --- multi-register writes: these fill out, out+1, out+2 ---
    /// <summary>(out, out+1, out+2) = hsv2rgb(a, b, c)</summary>
    HsvToRgb,

    /// <summary>(out, out+1, out+2) = previous frame sampled at (a, b)</summary>
    SampleFeedback,
}
