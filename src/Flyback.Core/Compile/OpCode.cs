namespace Flyback.Core.Compile;

/// <summary>
/// Instruction set of the scalar register machine a patch compiles down to.
/// Every op reads from and writes to <c>float</c> registers, so the whole program
/// is a flat, allocation-free list that can be walked per pixel — and each op
/// maps to one line of shader code.
/// </summary>
/// <remarks>
/// Numbered by hand, and a number once given is never given to anything else. A
/// module names these, and the compiler writes the number rather than the name
/// into whatever it builds — so an op slipped in among the others would renumber
/// every one after it in the host, and in no plugin that was already built. New
/// ops take the next number, wherever in the list they read best.
/// </remarks>
public enum OpCode : byte
{
    /// <summary>out = K</summary>
    Const = 0,

    /// <summary>out = pixel x coordinate</summary>
    LoadX = 1,

    /// <summary>out = pixel y coordinate</summary>
    LoadY = 2,

    /// <summary>out = current time in seconds</summary>
    LoadT = 3,

    /// <summary>out = how far x reaches, which is half the frame's width in y's units</summary>
    /// <remarks>
    /// y is always -1 to 1 and x is -this to this, so a module that wants to reach
    /// the left and right edges needs the number and cannot work it out from a
    /// pixel. It is 1 wherever there is no frame. A load rather than a folded
    /// constant, because one program is drawn at preview size, at export size and
    /// into a movie.
    /// </remarks>
    LoadAspect = 4,

    /// <summary>out = live input K, which is 0 wherever nothing is playing one</summary>
    /// <remarks>
    /// The one op whose answer comes from outside the program and outside the
    /// patch: it reads what somebody is doing to a keyboard right now.
    /// <para>
    /// K is a position in <c>CompiledPatch.LiveInputs</c> — which signal of
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
    LoadLive = 5,

    /// <summary>out = a</summary>
    Copy = 6,

    // --- unary ---
    Neg = 7,
    Abs = 8,
    Sin = 9,
    Cos = 10,
    Tan = 11,
    Sqrt = 12,
    Floor = 13,
    Ceil = 14,
    Fract = 15,
    Sign = 16,
    Exp = 17,
    Log = 18,

    // --- binary ---
    Add = 19,
    Sub = 20,
    Mul = 21,
    Div = 22,
    Mod = 23,
    Pow = 24,
    Min = 25,
    Max = 26,
    Atan2 = 27,

    /// <summary>out = b &lt; a ? 0 : 1 (GLSL step(edge: a, x: b))</summary>
    Step = 28,

    /// <summary>out = sqrt(a*a + b*b)</summary>
    Hypot = 29,

    // --- ternary ---
    /// <summary>out = clamp(a, b, c)</summary>
    Clamp = 30,

    /// <summary>out = a + (b - a) * c</summary>
    Mix = 31,

    /// <summary>out = smoothstep(edge0: a, edge1: b, x: c)</summary>
    Smoothstep = 32,

    /// <summary>out = value noise at (a, b, c)</summary>
    Noise3 = 33,

    // --- stateful: these remember something from the last evaluation, and are
    //     the only ops that do. The video path renders pixels in parallel and
    //     out of order, so it passes no state and each of them falls back to
    //     something total there rather than refusing to compile.

    /// <summary>
    /// out = line[now - c seconds], then line writes a + clamp(b) * out. A
    /// feedback comb: the delay itself, and the building block of a reverb. K is
    /// the longest delay this instance will ask for, which sizes the buffer.
    /// </summary>
    Delay = 34,

    /// <summary>
    /// out = line[now - c] - b * v, where v = a + b * line[now - c] is what gets
    /// written. A Schroeder allpass: it smears a signal in time without coloring
    /// it, which is what turns a bank of combs into a reverb rather than an echo.
    /// </summary>
    Allpass = 35,

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
    Phase = 36,

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
    UnitRead = 37,

    /// <summary>
    /// slot K = a. The one op that writes no register at all, because what it
    /// writes is read by the next evaluation's <see cref="UnitRead"/>.
    /// <c>Out</c> is -1 to say so.
    /// </summary>
    UnitWrite = 38,

    /// <summary>
    /// out = what plane K held at this pixel when the previous evaluation of it
    /// finished, and zero before there has been one.
    /// </summary>
    /// <remarks>
    /// <see cref="UnitRead"/> for a cell that both sinks can keep. A cell is one
    /// number, which is all the speakers need and nothing the screen can use: a
    /// picture is half a million evaluations of the program, each of them a
    /// different pixel, and one cell between them would be whatever pixel ran
    /// last. A plane is one number per pixel, so a pixel reads what it left.
    /// <para>
    /// Strictly its own pixel, which is what makes it affordable. Nothing else
    /// can see the value, so the renderer overwrites it in place and rows stay
    /// independent — where <see cref="SampleFeedback"/> reads at any coordinate
    /// and therefore costs a second copy of the frame. Reading elsewhere is what
    /// that op is for.
    /// </para>
    /// <para>
    /// One evaluation apart means a sample to the ear and a frame to the eye,
    /// the relation the two sinks already have. K is a slot number, as
    /// <see cref="UnitRead"/>'s is and for the same reason.
    /// </para>
    /// </remarks>
    PlaneRead = 39,

    /// <summary>
    /// plane K at this pixel = a, writing no register — <see cref="UnitWrite"/>
    /// for a plane, bounded the same way.
    /// </summary>
    PlaneWrite = 40,

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
    ClockWrite = 41,

    /// <summary>
    /// out = clip K at a seconds from its start, interpolated, and silence either
    /// side of it.
    /// </summary>
    /// <remarks>
    /// K is which clip rather than how long a buffer is, and the audio behind it
    /// is carried by the program — see <c>CompiledPatch.Tables</c> —
    /// because it is the same for every evaluation and every renderer. Not
    /// stateful: a clip is a function of the position asked for. A program
    /// compiled with no clips reads silence, which is what the shader does and
    /// what the screen gets.
    /// </remarks>
    Table = 42,

    /// <summary>
    /// trace K keeps a, and nothing is written to a register.
    /// </summary>
    /// <remarks>
    /// The one op whose whole purpose is outside the program: it hands a value to
    /// whoever is watching and produces nothing. A Scope is the only module that
    /// emits one — see <c>DelayState.Tap</c>.
    /// <para>
    /// It is also the one op that makes a program larger than what it computes.
    /// The compiler roots at every tap as well as at the sink, which keeps its
    /// input alive on a path that has no other use for it — ADR-0022's dead-code
    /// elimination given up on purpose, for the one thing that cannot work
    /// without it.
    /// </para>
    /// </remarks>
    Tap = 43,

    // --- multi-register writes: these fill out, out+1, out+2 ---
    /// <summary>(out, out+1, out+2) = hsv2rgb(a, b, c)</summary>
    HsvToRgb = 44,

    /// <summary>(out, out+1, out+2) = previous frame sampled at (a, b)</summary>
    SampleFeedback = 45,

    /// <summary>
    /// (out, out+1, out+2) = picture K sampled at (a, b), and black off its edges.
    /// K is a position in <c>CompiledPatch.Pictures</c>.
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
    SamplePicture = 46,
}
