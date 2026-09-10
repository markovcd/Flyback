namespace Flyback.Core.Compile;

/// <summary>
/// Instruction set of the scalar register machine a patch compiles down to.
/// Every op reads from and writes to <c>float</c> registers, so the whole program
/// is a flat, allocation-free list that can be walked per pixel — and each op
/// maps to one line of shader code.
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

    /// <summary>out = how far x reaches, which is half the frame's width in y's units</summary>
    /// <remarks>
    /// y is always -1 to 1 and x is -this to this, so a module that wants to reach
    /// the left and right edges needs the number and cannot work it out from a
    /// pixel. It is 1 wherever there is no frame. A load rather than a folded
    /// constant, because one program is drawn at preview size, at export size and
    /// into a movie.
    /// </remarks>
    LoadAspect,

    /// <summary>out = live input K, which is 0 wherever nothing is playing one</summary>
    /// <remarks>
    /// The one op whose answer comes from outside the program and outside the
    /// patch: it reads what somebody is doing to a keyboard right now.
    /// <para>
    /// K is a position in <see cref="CompiledPatch.LiveInputs"/> — which signal of
    /// which instrument, named there by a string. Named rather than numbered
    /// because the two ends never meet: a module asks for "keyboard/gate" while it
    /// is compiled, and something outside fills that in as a key moves.
    /// </para>
    /// <para>
    /// Not stateful, though it sits beside the ops that are: where no block is
    /// passed it reads zero, which is what an offline render gets and is the
    /// honest answer — nobody was playing.
    /// </para>
    /// </remarks>
    LoadLive,

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
    /// out = line[now - c seconds], then line writes a + clamp(b) * out. A
    /// feedback comb: the delay itself, and the building block of a reverb. K is
    /// the longest delay this instance will ask for, which sizes the buffer.
    /// </summary>
    Delay,

    /// <summary>
    /// out = line[now - c] - b * v, where v = a + b * line[now - c] is what gets
    /// written. A Schroeder allpass: it smears a signal in time without coloring
    /// it, which is what turns a bank of combs into a reverb rather than an echo.
    /// </summary>
    Allpass,

    /// <summary>
    /// out = fract(phase + (a - a_previous) * b) + c, where phase is carried from
    /// the last evaluation: the running total of how far the domain 'a' has moved,
    /// counted in cycles of 'b' as it was at each step, with 'c' added afterwards
    /// rather than integrated.
    /// <para>
    /// Integrating is what makes a frequency change silent. A plain phase = a * b
    /// jumps by a times the change in b, so a stepped pitch tears the waveform by
    /// more the longer the patch has run; accumulated, the phase moves by one
    /// step's worth however far b jumps, and only the slope changes.
    /// </para>
    /// <para>
    /// Without state it falls back to a * b + c, which is a picture: one
    /// evaluation per pixel, with no previous sample to carry anything from.
    /// </para>
    /// </summary>
    Phase,

    /// <summary>
    /// out = the value slot K held when the previous evaluation finished, and
    /// zero before there has been one.
    /// </summary>
    /// <remarks>
    /// Half of a pair, and the half that stands where the graph wants a value. Its
    /// <see cref="UnitWrite"/> is emitted after every read in the program, so a
    /// read is always one evaluation old however the wires run — which is what
    /// lets a patch hold a loop at all.
    /// <para>
    /// K here is a slot number rather than a length: the read and the write must
    /// agree about which cell they mean, and counting positions the way
    /// <see cref="Delay"/> does would rest that agreement on emit order, which for
    /// these two is deliberately not the same.
    /// </para>
    /// </remarks>
    UnitRead,

    /// <summary>
    /// slot K = a. The one op that writes no register at all, because what it
    /// writes is read by the next evaluation's <see cref="UnitRead"/>.
    /// <c>Out</c> is -1 to say so.
    /// </summary>
    UnitWrite,

    /// <summary>
    /// slot K = a, unbounded. <see cref="UnitWrite"/> for a cell holding the
    /// renderer's clock rather than a signal from the patch.
    /// </summary>
    /// <remarks>
    /// The two differ only in the bound. A cell a patch can draw a wire into may
    /// be part of a loop with a gain above one, so what goes in is clamped to the
    /// rails. A clock cannot run away but does pass any bound by the patch being
    /// left playing, and clamped it sticks — leaving every module that measures
    /// its own rate off it with a rate that grows without end.
    /// </remarks>
    ClockWrite,

    /// <summary>
    /// out = clip K at a seconds from its start, interpolated, and silence either
    /// side of it.
    /// </summary>
    /// <remarks>
    /// K is which clip rather than how long a buffer is, and the audio behind it
    /// is carried by the program — see <see cref="CompiledPatch.Tables"/> —
    /// because it is the same for every evaluation and every renderer. Not
    /// stateful: a clip is a function of the position asked for. A program
    /// compiled with no clips reads silence, which is what the shader does and
    /// what the screen gets.
    /// </remarks>
    Table,

    /// <summary>
    /// trace K keeps a, and nothing is written to a register.
    /// </summary>
    /// <remarks>
    /// The one op whose whole purpose is outside the program: it hands a value to
    /// whoever is watching and produces nothing. A Scope is the only module that
    /// emits one — see <see cref="DelayState.Tap"/>.
    /// <para>
    /// It is also the one op that makes a program larger than what it computes.
    /// The compiler roots at every tap as well as at the sink, which keeps its
    /// input alive on a path that has no other use for it — ADR-0022's dead-code
    /// elimination given up on purpose, for the one thing that cannot work
    /// without it.
    /// </para>
    /// </remarks>
    Tap,

    // --- multi-register writes: these fill out, out+1, out+2 ---
    /// <summary>(out, out+1, out+2) = hsv2rgb(a, b, c)</summary>
    HsvToRgb,

    /// <summary>(out, out+1, out+2) = previous frame sampled at (a, b)</summary>
    SampleFeedback,

    /// <summary>
    /// (out, out+1, out+2) = picture K sampled at (a, b), and black off its edges.
    /// K is a position in <see cref="CompiledPatch.Pictures"/>.
    /// </summary>
    /// <remarks>
    /// A file, named by the patch and loaded before any of this ran.
    /// <see cref="Table"/> is its counterpart for the ear and is the op that
    /// cannot be drawn — a clip is a buffer the shader has nowhere to put — where
    /// a texture is what a shader is made to read, so this stays on both backends.
    /// The picture is placed at its own shape, spanning -1 to 1 down the frame
    /// with black beyond — see <see cref="LoadedImage.At"/>, which is what this
    /// lowers to on the processor and what the shader agrees with.
    /// </remarks>
    SamplePicture,
}
