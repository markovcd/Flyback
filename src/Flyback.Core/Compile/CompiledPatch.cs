using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyback.Core.Compile;

/// <summary>
/// The previous frame, exposed to <see cref="OpCode.SampleFeedback"/>. Stored
/// as linear float RGB so repeated feedback passes don't quantise to 8 bits.
/// </summary>
public readonly struct FeedbackFrame(float[]? pixels, int width, int height)
{
    public readonly float[]? Pixels = pixels;
    public readonly int Width = width;
    public readonly int Height = height;

    public float Aspect => Height == 0 ? 1f : (float)Width / Height;
}

/// <summary>
/// A patch lowered to a straight-line program. Evaluating it for a pixel means
/// walking <see cref="Ops"/> once, so there is no graph traversal, no virtual
/// dispatch and no allocation in the inner loop.
/// </summary>
/// <remarks>
/// Registers are <see cref="double"/>. Nothing a sink produces needs the extra
/// mantissa — a pixel is eight bits and a sample is sixteen — but the *domain*
/// does: on the audio path the machine runs at 192 kHz against a clock that
/// keeps counting, and past about a minute a <see cref="float"/> cannot hold
/// two consecutive sample times apart. See ADR-0032. The width costs nothing
/// measurable, because this loop is bound by its own dispatch rather than by
/// the arithmetic in it.
/// </remarks>
public sealed class CompiledPatch(
    Op[] ops,
    int registerCount,
    int outputBase,
    int outputWidth = 3,
    IReadOnlyList<LoadedSample>? tables = null,
    IReadOnlyList<TapSpec>? taps = null,
    IReadOnlyList<string>? liveInputs = null,
    IReadOnlyList<LoadedImage>? pictures = null,
    StateOwners? owners = null)
{
    public Op[] Ops { get; } = ops;

    /// <summary>
    /// Which node owns each cell of memory this program keeps, so a swap can
    /// hand a module back its own rather than whatever now sits in the same
    /// slot — see <see cref="StateOwners"/>.
    /// </summary>
    /// <remarks>
    /// Empty for a program assembled by hand, which is exactly what it should
    /// be: nothing is claimed, so nothing is adopted, and such a program starts
    /// from silence the way it always did.
    /// </remarks>
    public StateOwners Owners { get; } = owners ?? StateOwners.None;

    /// <summary>
    /// What this program is played with: the live inputs
    /// <see cref="OpCode.LoadLive"/> reads, in the order its K numbers them.
    /// </summary>
    /// <remarks>
    /// Names rather than values, and that is the whole of the join: a program
    /// says which signals of which instrument it wants, and whoever is running it
    /// builds a <see cref="LiveValues"/> from this list and fills that in as the
    /// keys move. Empty for the great majority of patches, which are played by
    /// nothing but their own clock.
    /// </remarks>
    public IReadOnlyList<string> LiveInputs { get; } = liveInputs ?? [];

    /// <summary>
    /// How many live inputs the ops actually read, which is what a backend has to
    /// make room for.
    /// </summary>
    /// <remarks>
    /// Counted from the highest K rather than from <see cref="LiveInputs"/>, the
    /// way <see cref="TraceCount"/> and <see cref="UnitCount"/> are counted from
    /// theirs. The two agree for every program the compiler builds, and the
    /// difference is what keeps a program assembled by hand — a test, or a tool
    /// writing ops directly — from producing a shader that reads past the end of
    /// an array it declared.
    /// </remarks>
    public int LiveCount { get; } = Math.Max(
        liveInputs?.Count ?? 0,
        ops.Where(o => o.Code is OpCode.LoadLive)
            .Select(o => (int)o.K + 1)
            .DefaultIfEmpty(0)
            .Max());

    /// <summary>
    /// The Scopes this program has something to do with, in the order
    /// <see cref="OpCode.Tap"/> numbers them.
    /// </summary>
    /// <remarks>
    /// Both programs of a patch carry this and they mean opposite halves of it.
    /// The speakers' program has one tap op per entry and no buffer; the
    /// screen's program has one table read per entry and no tap. What pairs them
    /// is the node id, because the two are compiled separately and throw away
    /// different dead code — see <see cref="Traces.Refresh"/>.
    /// </remarks>
    public IReadOnlyList<TapSpec> Taps { get; } = taps ?? [];

    /// <summary>
    /// The clips <see cref="OpCode.Table"/> reads, indexed by its K.
    /// </summary>
    /// <remarks>
    /// Carried by the program rather than handed to it per evaluation, unlike a
    /// delay line: a clip is the same for every evaluation and for every
    /// renderer, and there is nothing to allocate fresh. Empty where the patch
    /// asked for none, and empty on the video path whatever it asked for — see
    /// OpCode.Table.
    /// </remarks>
    public IReadOnlyList<LoadedSample> Tables => tableArray;

    /// <summary>
    /// The pictures <see cref="OpCode.SamplePicture"/> reads, indexed by its K.
    /// </summary>
    /// <remarks>
    /// Carried the way <see cref="Tables"/> is, and unlike it on the one point
    /// that matters: this list is filled on the <em>video</em> path, since a
    /// picture is a thing to look at. What the speakers make of an Image is one
    /// evaluation of a still, which is a color that never moves, so the audio
    /// program carries none of these and reads black.
    /// <para>
    /// It is also what the shader is handed before a frame: one texture per
    /// entry, in this order, which is how a uniform finds the picture its op
    /// names.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LoadedImage> Pictures => pictureArray;


    /// <summary>
    /// <see cref="Tables"/> and <see cref="Pictures"/> as what the interpreter
    /// indexes, rather than as what a caller reads them through.
    /// </summary>
    /// <remarks>
    /// An <see cref="IReadOnlyList{T}"/> indexer is an interface call, and both
    /// of these are read inside the per-pixel loop — a patch with an Image in it
    /// would pay for one on every pixel of every frame, to reach an array that
    /// was already an array. The properties above still hand out the list, so
    /// nothing outside this class notices.
    /// </remarks>
    private readonly LoadedSample[] tableArray = tables as LoadedSample[] ?? [.. tables ?? []];

    private readonly LoadedImage[] pictureArray = pictures as LoadedImage[] ?? [.. pictures ?? []];

    public int RegisterCount { get; } = Vouch(ops, registerCount);

    /// <summary>
    /// The same ops sorted by how often a frame has to run them, or null for a
    /// program that cannot be sorted. See <see cref="FramePlan"/>.
    /// </summary>
    /// <remarks>
    /// Worked out once here rather than by whoever is drawing, because it is a
    /// property of the program and the program is what outlives a frame. A patch
    /// is recompiled on every edit and drawn sixty times a second in between, so
    /// the walk this costs is paid once against the half a million evaluations
    /// it saves each of those frames.
    /// </remarks>
    public FramePlan? Plan { get; } = FramePlan.For(ops, registerCount);

    /// <summary>First of the <see cref="OutputWidth"/> registers holding the result.</summary>
    public int OutputBase { get; } = outputBase;

    /// <summary>3 for a video sink's RGB, 2 for an audio sink's stereo pair.</summary>
    public int OutputWidth { get; } = outputWidth;

    /// <summary>
    /// The longest delay each stateful op will ask for, in the order those ops
    /// run. A renderer sizes one ring buffer per entry; a program with none — the
    /// usual case — needs no state at all.
    /// </summary>
    public IReadOnlyList<float> DelayLengths { get; } =
        [.. ops.Where(o => o.Code is OpCode.Delay or OpCode.Allpass).Select(o => o.K)];

    /// <summary>
    /// How many traces this program keeps — one per Scope whose input it
    /// evaluates, which is the speakers' program and no other.
    /// </summary>
    /// <remarks>
    /// Counted from the highest slot rather than from how many taps there are,
    /// because a scope wired to nothing emits none and would otherwise shift
    /// every scope after it.
    /// </remarks>
    public int TraceCount { get; } = ops
        .Where(o => o.Code is OpCode.Tap)
        .Select(o => (int)o.K + 1)
        .DefaultIfEmpty(0)
        .Max();

    /// <summary>
    /// How many phase accumulators the program runs. One cell each, so unlike a
    /// delay line there is nothing to size — but a program with any of these
    /// still needs state, and a renderer that gave it none would hand every
    /// oscillator back its multiply.
    /// </summary>
    public int PhaseCount { get; } = ops.Count(o => o.Code is OpCode.Phase);

    /// <summary>
    /// How many one-evaluation cells the program needs — one per cycle in the
    /// patch it came from.
    /// </summary>
    /// <remarks>
    /// Taken from the highest slot any op names rather than from how many ops name
    /// one, because a read and its write are two ops sharing a cell and counting
    /// them would count each cell twice.
    /// </remarks>
    public int UnitCount { get; } = ops
        .Where(o => o.Code is OpCode.UnitRead or OpCode.UnitWrite or OpCode.ClockWrite)
        .Select(o => (int)o.K + 1)
        .DefaultIfEmpty(0)
        .Max();

    /// <summary>
    /// A program whose output is all zeroes — what the compiler falls back to
    /// for a graph with no Output at all, which means one assembled by hand
    /// rather than through <see cref="Graph.Patch.EnsureOutput"/>.
    /// </summary>
    public static CompiledPatch Constant(int width) => new(
        [.. Enumerable.Range(0, width).Select(i => new Op(OpCode.Const, i))],
        width,
        0,
        width);

    /// <summary>Renders nothing. What a preview holds before a patch has been compiled into it.</summary>
    public static CompiledPatch Black { get; } = Constant(3);

    /// <summary>Plays nothing. What the audio engine starts on, before a patch reaches it.</summary>
    public static CompiledPatch Silent { get; } = Constant(2);

    public double[] AllocateRegisters() => new double[Math.Max(RegisterCount, OutputWidth)];

    /// <summary>Runs the program for one pixel. <paramref name="registers"/> is reused across pixels.</summary>
    /// <param name="feedback">The frame before this one, for <see cref="OpCode.SampleFeedback"/> to read. Empty on the first frame and off the audio path, where a sample has no previous picture.</param>
    /// <param name="delays">
    /// Memory for the stateful ops, or null when there is none. Null is not a
    /// failure: it is what the video path passes, because rows render in
    /// parallel and a shared delay line has no meaning per pixel. Without it a
    /// delay hands its input straight through, so a patch built for the speakers
    /// still shows a picture.
    /// </param>
    /// <param name="x">Horizontal position, widened by the aspect ratio. Pinned to zero on the audio path.</param>
    /// <param name="y">Vertical position, -1 at the bottom to 1 at the top. Pinned to zero on the audio path.</param>
    /// <param name="t">Seconds since the patch started, which is the only one of the three that moves for the ear.</param>
    /// <param name="registers">Scratch for the whole program, sized by <see cref="RegisterCount"/> and reused across pixels rather than allocated per one.</param>
    /// <param name="aspect">
    /// How far <paramref name="x"/> reaches at the edge of the frame, for
    /// <see cref="OpCode.LoadAspect"/>. Defaults to a square picture, which is
    /// what a caller that is not drawing one has.
    /// </param>
    /// <param name="live">
    /// What is being played into the program from outside it, or null when
    /// nothing is. Null is the ordinary case rather than a failure — an offline
    /// render, a test and a headless compile all have nobody at the keys — and
    /// there every live input reads zero, which is what an instrument nobody is
    /// touching does.
    /// </param>
    public void Evaluate(
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        DelayState? delays = null,
        double aspect = 1d,
        LiveValues? live = null) =>
        Run(Ops, 0, Ops.Length, x, y, t, registers, feedback, delays, aspect, live);

    /// <summary>
    /// Runs only the ops of <paramref name="stage"/>, for a caller drawing a
    /// frame that means to run each stage where it belongs.
    /// </summary>
    /// <remarks>
    /// The three stages together do exactly what one <see cref="Evaluate"/>
    /// does, provided they are run in order into the same register bank and the
    /// arguments a stage does not vary with are held still across it — see
    /// <see cref="FramePlan"/> for why that is safe and what it saves.
    /// <para>
    /// A program with no plan runs whole at <see cref="EvaluationStage.Pixel"/>
    /// and does nothing at the other two, so a caller staging its loops gets the
    /// right picture either way and pays only for what the program allowed.
    /// </para>
    /// </remarks>
    public void EvaluateStage(
        EvaluationStage stage,
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        double aspect = 1d,
        LiveValues? live = null)
    {
        if (Plan is not { } plan)
        {
            if (stage is EvaluationStage.Pixel)
                Run(Ops, 0, Ops.Length, x, y, t, registers, feedback, null, aspect, live);

            return;
        }

        var (from, to) = plan.Range(stage);

        Run(plan.Ops, from, to, x, y, t, registers, feedback, null, aspect, live);
    }

    /// <summary>
    /// Walks <paramref name="ops"/> from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// A range rather than the whole array, so the staged path can run a third
    /// of the program without a second copy of the switch. Only a whole run may
    /// pass <paramref name="delays"/>: which line or cell a stateful op uses is
    /// counted from the start of the program, so a run that begins part way
    /// through would count from the wrong place — and a staged run passes none,
    /// which is also what lets its ops be reordered at all.
    /// </remarks>
    private void Run(
        Op[] ops,
        int from,
        int to,
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        DelayState? delays,
        double aspect,
        LiveValues? live)
    {
        if (registers.Length < RegisterCount)
            throw new ArgumentException(
                $"Register bank holds {registers.Length}, the program needs {RegisterCount}.",
                nameof(registers));

        // The one place the register file is touched without a bounds check, and
        // what pays for it is the constructor: every index below was checked
        // against RegisterCount once, when the program was made, and the guard
        // above is the other half of that — together they say the bank is at
        // least as long as the largest index any op names. Checking per access
        // instead costs four compares on an Add, which at thirty ops a pixel and
        // half a million pixels a frame is most of what this loop does.
        ref var bank = ref MemoryMarshal.GetReference(registers);

        // Which line or cell an op uses is its position among the ops of its
        // kind, so each kind is counted on its own.
        var line = 0;
        var cell = 0;

        for (var index = from; index < to; index++)
        {
            ref readonly var op = ref ops[index];

            switch (op.Code)
            {
                case OpCode.Const: Reg(ref bank, op.Out) = op.K; break;
                case OpCode.LoadX: Reg(ref bank, op.Out) = x; break;
                case OpCode.LoadY: Reg(ref bank, op.Out) = y; break;
                case OpCode.LoadT: Reg(ref bank, op.Out) = t; break;
                case OpCode.LoadAspect: Reg(ref bank, op.Out) = aspect; break;
                case OpCode.LoadLive: Reg(ref bank, op.Out) = live?.At((int)op.K) ?? 0d; break;
                case OpCode.Copy: Reg(ref bank, op.Out) = Reg(ref bank, op.A); break;

                case OpCode.Neg: Reg(ref bank, op.Out) = -Reg(ref bank, op.A); break;
                case OpCode.Abs: Reg(ref bank, op.Out) = Math.Abs(Reg(ref bank, op.A)); break;
                case OpCode.Sin: Reg(ref bank, op.Out) = Math.Sin(Reg(ref bank, op.A)); break;
                case OpCode.Cos: Reg(ref bank, op.Out) = Math.Cos(Reg(ref bank, op.A)); break;
                case OpCode.Tan: Reg(ref bank, op.Out) = Guard(Math.Tan(Reg(ref bank, op.A))); break;
                case OpCode.Sqrt:
                {
                    var a = Reg(ref bank, op.A);
                    Reg(ref bank, op.Out) = a <= 0d ? 0d : Math.Sqrt(a);
                    break;
                }

                case OpCode.Floor: Reg(ref bank, op.Out) = Math.Floor(Reg(ref bank, op.A)); break;
                case OpCode.Ceil: Reg(ref bank, op.Out) = Math.Ceiling(Reg(ref bank, op.A)); break;
                case OpCode.Fract: Reg(ref bank, op.Out) = Fract(Reg(ref bank, op.A)); break;
                case OpCode.Sign: Reg(ref bank, op.Out) = Math.Sign(Reg(ref bank, op.A)); break;
                case OpCode.Exp: Reg(ref bank, op.Out) = Guard(Math.Exp(Reg(ref bank, op.A))); break;
                case OpCode.Log:
                {
                    var a = Reg(ref bank, op.A);
                    Reg(ref bank, op.Out) = a <= 0d ? 0d : Math.Log(a);
                    break;
                }

                case OpCode.Add: Reg(ref bank, op.Out) = Reg(ref bank, op.A) + Reg(ref bank, op.B); break;
                case OpCode.Sub: Reg(ref bank, op.Out) = Reg(ref bank, op.A) - Reg(ref bank, op.B); break;
                case OpCode.Mul: Reg(ref bank, op.Out) = Reg(ref bank, op.A) * Reg(ref bank, op.B); break;
                case OpCode.Div: Reg(ref bank, op.Out) = Divide(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Mod: Reg(ref bank, op.Out) = Modulo(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Pow: Reg(ref bank, op.Out) = Guard(Math.Pow(Reg(ref bank, op.A), Reg(ref bank, op.B))); break;
                case OpCode.Min: Reg(ref bank, op.Out) = Math.Min(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Max: Reg(ref bank, op.Out) = Math.Max(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Atan2: Reg(ref bank, op.Out) = Math.Atan2(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Step: Reg(ref bank, op.Out) = Reg(ref bank, op.B) < Reg(ref bank, op.A) ? 0d : 1d; break;
                case OpCode.Hypot:
                {
                    double a = Reg(ref bank, op.A), b = Reg(ref bank, op.B);
                    Reg(ref bank, op.Out) = Math.Sqrt(a * a + b * b);
                    break;
                }

                case OpCode.Clamp:
                {
                    double a = Reg(ref bank, op.A), b = Reg(ref bank, op.B), c = Reg(ref bank, op.C);
                    Reg(ref bank, op.Out) = Math.Clamp(a, b, Math.Max(b, c));
                    break;
                }

                case OpCode.Mix:
                {
                    double a = Reg(ref bank, op.A), b = Reg(ref bank, op.B), f = Reg(ref bank, op.C);
                    Reg(ref bank, op.Out) = a + (b - a) * f;
                    break;
                }

                case OpCode.Smoothstep:
                    Reg(ref bank, op.Out) = Smoothstep(Reg(ref bank, op.A), Reg(ref bank, op.B), Reg(ref bank, op.C));
                    break;

                case OpCode.Noise3:
                    Reg(ref bank, op.Out) = Noise.Value3(Reg(ref bank, op.A), Reg(ref bank, op.B), Reg(ref bank, op.C));
                    break;

                case OpCode.HsvToRgb:
                    HsvToRgb(
                        Reg(ref bank, op.A),
                        Reg(ref bank, op.B),
                        Reg(ref bank, op.C),
                        Triple(ref bank, op.Out));
                    break;

                case OpCode.SampleFeedback:
                    Sample(feedback, Reg(ref bank, op.A), Reg(ref bank, op.B), Triple(ref bank, op.Out));
                    break;

                case OpCode.SamplePicture:
                {
                    var picture = (int)op.K;
                    var rgb = Triple(ref bank, op.Out);

                    // Black where the program carries no pictures, which is
                    // every audio program and any video one whose file was not
                    // there — the same answer a Table gives silence for.
                    if ((uint)picture < (uint)pictureArray.Length)
                        pictureArray[picture].At(Reg(ref bank, op.A), Reg(ref bank, op.B), rgb);
                    else
                        rgb[0] = rgb[1] = rgb[2] = 0d;

                    break;
                }

                case OpCode.Tap:
                    delays?.Tap((int)op.K, Reg(ref bank, op.A));
                    break;

                case OpCode.Table:
                {
                    var clip = (int)op.K;

                    // Silence where the program carries no clips, which is every
                    // video program and any audio one whose file was not there.
                    Reg(ref bank, op.Out) = (uint)clip < (uint)tableArray.Length
                        ? tableArray[clip].At(Reg(ref bank, op.A))
                        : 0d;
                    break;
                }

                case OpCode.Delay:
                {
                    var slot = line++;
                    if (delays is null) { Reg(ref bank, op.Out) = Reg(ref bank, op.A); break; }

                    // Read before write, so the shortest possible delay is one
                    // evaluation. A zero-sample loop would be algebraic, and
                    // there would be nothing for it to mean.
                    var heard = delays.Read(slot, Reg(ref bank, op.C), op.K);
                    delays.Write(slot, Reg(ref bank, op.A) + Feedback(Reg(ref bank, op.B)) * heard);
                    Reg(ref bank, op.Out) = heard;
                    break;
                }

                case OpCode.Allpass:
                {
                    var slot = line++;
                    if (delays is null) { Reg(ref bank, op.Out) = Reg(ref bank, op.A); break; }

                    var heard = delays.Read(slot, Reg(ref bank, op.C), op.K);
                    var gain = Feedback(Reg(ref bank, op.B));
                    var stored = Reg(ref bank, op.A) + gain * heard;

                    delays.Write(slot, stored);
                    Reg(ref bank, op.Out) = heard - gain * stored;
                    break;
                }

                // The two halves of a cycle. Without state a read is zero and a
                // write goes nowhere, so a loop drawn on the video path is simply
                // open — the same fallback the delay lines take, for the same
                // reason: pixels are evaluated in parallel and in whatever order,
                // and there is no "previous evaluation" for one to mean.
                case OpCode.UnitRead:
                    Reg(ref bank, op.Out) = delays?.ReadUnit((int)op.K) ?? 0d;
                    break;

                case OpCode.UnitWrite:
                    delays?.WriteUnit((int)op.K, Reg(ref bank, op.A));
                    break;

                case OpCode.ClockWrite:
                    delays?.WriteClock((int)op.K, Reg(ref bank, op.A));
                    break;

                case OpCode.Phase:
                {
                    var slot = cell++;
                    double input = Reg(ref bank, op.A), frequency = Reg(ref bank, op.B);

                    // Without state there is no previous evaluation to step from
                    // — a picture's pixels are one evaluation each, in whatever
                    // order the rows happen to run — so this is the multiply the
                    // accumulator replaces, and over a still frame the two agree.
                    Reg(ref bank, op.Out) = delays is null
                        ? input * frequency + Reg(ref bank, op.C)
                        : delays.Advance(slot, input, frequency) + Reg(ref bank, op.C);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Checks, once, that no op names a register outside a bank of
    /// <paramref name="registerCount"/>, and hands that count back so it can be
    /// the property's initialiser.
    /// </summary>
    /// <remarks>
    /// What makes the unchecked reads in <see cref="Evaluate"/> safe, and the
    /// reason it is worth doing here: a program is walked once when it is made
    /// and several million times after that, so the same check costs nothing
    /// where it is and most of the inner loop where it was.
    /// <para>
    /// Only the fields an op actually reads — <see cref="OpShape"/> says which.
    /// An op that takes no operand leaves A, B and C at -1, and that is not a
    /// register out of range but the absence of one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An op names a register the bank does not hold, which is a malformed
    /// program rather than a bad input — caught here, where the message can name
    /// the instruction, instead of as a memory fault a million evaluations later.
    /// </exception>
    private static int Vouch(Op[] ops, int registerCount)
    {
        ArgumentNullException.ThrowIfNull(ops);

        for (var i = 0; i < ops.Length; i++)
        {
            var op = ops[i];
            var inputs = OpShape.Inputs(op.Code);

            if (inputs > 0 && !Holds(op.A, 1)) throw Malformed(i, op, op.A, 1);
            if (inputs > 1 && !Holds(op.B, 1)) throw Malformed(i, op, op.B, 1);
            if (inputs > 2 && !Holds(op.C, 1)) throw Malformed(i, op, op.C, 1);

            var width = OpShape.Outputs(op.Code);
            if (width > 0 && !Holds(op.Out, width)) throw Malformed(i, op, op.Out, width);
        }

        return registerCount;

        bool Holds(int register, int width) => register >= 0 && register + width <= registerCount;

        ArgumentException Malformed(int at, Op op, int register, int width) => new(
            $"Op {at} ({op}) names registers {register}..{register + width - 1} "
            + $"of a bank holding {registerCount}.",
            nameof(ops));
    }


    /// <summary>Register <paramref name="index"/> of a bank the constructor has already vouched for.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref double Reg(ref double bank, int index) => ref Unsafe.Add(ref bank, index);

    /// <summary>The three consecutive registers a color-width op writes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Span<double> Triple(ref double bank, int first) =>
        MemoryMarshal.CreateSpan(ref Reg(ref bank, first), 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Guard(double v) => double.IsFinite(v) ? v : 0d;

    /// <summary>
    /// Feedback held below one. At exactly one a delay line never decays and at
    /// more than one it doubles every pass, and unlike every other op in here
    /// that damage persists after the knob is turned back down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Feedback(double v) => double.IsFinite(v) ? Math.Clamp(v, -0.99d, 0.99d) : 0d;

    /// <summary>
    /// The largest double below 1. For a tiny negative input, <c>v - floor(v)</c>
    /// is mathematically just under 1 but cancels to exactly 1.0 at any finite
    /// precision. Fract is documented as half-open, and Saw and Tile both read
    /// it that way, so the result is pinned just below the boundary instead of
    /// being allowed to reach it.
    /// </summary>
    private const double JustBelowOne = 0.99999999999999989d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Fract(double v)
    {
        var fraction = v - Math.Floor(v);
        return fraction < 1d ? fraction : JustBelowOne;
    }

    // Exact equality is the point in the three guards below: they trap the one
    // divisor that makes the result undefined. A tolerance would not be safer,
    // it would be wrong — Divide(1, 1e-20f) is a legitimate 1e20, and an epsilon
    // would silently flatten it to zero. Guard already handles the overflow.
    // ReSharper disable CompareOfFloatsByEqualityOperator

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Divide(double a, double b) => b == 0d ? 0d : Guard(a / b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Modulo(double a, double b) => b == 0d ? 0d : Guard(a - b * Math.Floor(a / b));

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge0 == edge1) return x < edge0 ? 0d : 1d;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    // ReSharper restore CompareOfFloatsByEqualityOperator

    private static void HsvToRgb(double h, double s, double v, Span<double> rgb)
    {
        h = Fract(h) * 6d;
        s = Math.Clamp(s, 0d, 1d);

        var sector = (int)h;
        var f = h - sector;
        var p = v * (1d - s);
        var q = v * (1d - s * f);
        var t = v * (1d - s * (1d - f));

        (rgb[0], rgb[1], rgb[2]) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }

    /// <summary>Bilinear read of the previous frame in patch coordinates, clamped at the edges.</summary>
    private static void Sample(in FeedbackFrame frame, double u, double v, Span<double> rgb)
    {
        var pixels = frame.Pixels;
        if (pixels is null || frame.Width < 2 || frame.Height < 2)
        {
            rgb[0] = rgb[1] = rgb[2] = 0d;
            return;
        }

        var fx = (u / frame.Aspect * 0.5d + 0.5d) * (frame.Width - 1);
        var fy = (0.5d - v * 0.5d) * (frame.Height - 1);

        fx = Math.Clamp(double.IsFinite(fx) ? fx : 0d, 0d, frame.Width - 1.001d);
        fy = Math.Clamp(double.IsFinite(fy) ? fy : 0d, 0d, frame.Height - 1.001d);

        int x0 = (int)fx, y0 = (int)fy;
        double tx = fx - x0, ty = fy - y0;

        var row0 = y0 * frame.Width;
        var row1 = row0 + frame.Width;
        var i00 = (row0 + x0) * 3;
        var i10 = i00 + 3;
        var i01 = (row1 + x0) * 3;
        var i11 = i01 + 3;

        for (var c = 0; c < 3; c++)
        {
            var top = pixels[i00 + c] + (pixels[i10 + c] - pixels[i00 + c]) * tx;
            var bottom = pixels[i01 + c] + (pixels[i11 + c] - pixels[i01 + c]) * tx;
            rgb[c] = top + (bottom - top) * ty;
        }
    }
}
