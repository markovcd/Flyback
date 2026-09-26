using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyback.Core.Compile;

/// <summary>
/// A patch lowered to a straight-line program. Evaluating it for a pixel means
/// walking <see cref="Ops"/> once, so there is no graph traversal, no virtual
/// dispatch and no allocation in the inner loop.
/// </summary>
/// <remarks>
/// Registers are <see cref="double"/> for the domain rather than the result: at
/// 192 kHz against a clock that keeps counting, a <see cref="float"/> cannot
/// hold two consecutive sample times apart past about a minute. See ADR-0032.
/// </remarks>
public sealed class CompiledPatch
{
    internal CompiledPatch(
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
        Ops = ops;
        Owners = owners ?? StateOwners.None;
        LiveInputs = liveInputs ?? [];
        LiveCount = Math.Max(
            liveInputs?.Count ?? 0,
            ops.Where(o => o.Code is OpCode.LoadLive)
                .Select(o => (int)o.K + 1)
                .DefaultIfEmpty(0)
                .Max());
        Taps = taps ?? [];
        RegisterCount = Vouch(ops, registerCount);
        Plan = FramePlan.For(ops, registerCount);
        OutputBase = outputBase;
        OutputWidth = outputWidth;
        DelayLengths =
            [.. ops.Where(o => o.Code is OpCode.Delay or OpCode.Allpass).Select(o => o.K)];
        TraceCount = ops
            .Where(o => o.Code is OpCode.Tap)
            .Select(o => (int)o.K + 1)
            .DefaultIfEmpty(0)
            .Max();
        PhaseCount = ops.Count(o => o.Code is OpCode.Phase);
        UnitCount = ops
            .Where(o => o.Code is OpCode.UnitRead or OpCode.UnitWrite or OpCode.ClockWrite)
            .Select(o => (int)o.K + 1)
            .DefaultIfEmpty(0)
            .Max();
        PlaneCount = ops
            .Where(o => o.Code is OpCode.PlaneRead or OpCode.PlaneWrite)
            .Select(o => (int)o.K + 1)
            .DefaultIfEmpty(0)
            .Max();
        tableArray = tables as LoadedSample[] ?? [.. tables ?? []];
        pictureArray = pictures as LoadedImage[] ?? [.. pictures ?? []];
    }

    internal Op[] Ops { get; }

    /// <summary>
    /// Which node owns each cell of memory this program keeps, so a swap can hand
    /// a module back its own rather than whatever sits in the same slot — see
    /// <see cref="StateOwners"/>. Empty for a program assembled by hand, which
    /// then starts from silence.
    /// </summary>
    internal StateOwners Owners { get; }

    /// <summary>
    /// What this program is played with: the live inputs
    /// <see cref="OpCode.LoadLive"/> reads, in the order its K numbers them.
    /// Names rather than values — whoever runs the program builds a
    /// <see cref="LiveValues"/> from this list and fills it in as the keys move.
    /// </summary>
    public IReadOnlyList<string> LiveInputs { get; }

    /// <summary>
    /// How many live inputs the ops actually read, which is what a backend has to
    /// make room for. Counted from the highest K rather than from
    /// <see cref="LiveInputs"/>, so a program assembled by hand cannot produce a
    /// shader that reads past the end of the array it declared.
    /// </summary>
    public int LiveCount { get; }

    /// <summary>
    /// The Scopes this program has something to do with, in the order
    /// <see cref="OpCode.Tap"/> numbers them.
    /// </summary>
    /// <remarks>
    /// Both programs of a patch carry this and mean opposite halves: the
    /// speakers' has one tap per entry and no buffer, the screen's one table read
    /// per entry and no tap. The node id pairs them, since the two are compiled
    /// separately and throw away different dead code — see
    /// <see cref="Traces.Refresh"/>.
    /// </remarks>
    public IReadOnlyList<TapSpec> Taps { get; }

    /// <summary>
    /// The clips <see cref="OpCode.Table"/> reads, indexed by its K. Carried by
    /// the program rather than handed to it per evaluation, unlike a delay line:
    /// a clip is the same for every evaluation and every renderer. Empty on the
    /// video path whatever the patch asked for — see OpCode.Table.
    /// </summary>
    public IReadOnlyList<LoadedSample> Tables => tableArray;

    /// <summary>
    /// The pictures <see cref="OpCode.SamplePicture"/> reads, indexed by its K.
    /// </summary>
    /// <remarks>
    /// Carried the way <see cref="Tables"/> is, but filled on the video path: what
    /// the speakers make of an Image is a color that never moves, so the audio
    /// program carries none and reads black. It is also what the shader is handed
    /// before a frame — one texture per entry, in this order.
    /// </remarks>
    public IReadOnlyList<LoadedImage> Pictures => pictureArray;


    /// <summary>
    /// <see cref="Tables"/> and <see cref="Pictures"/> as what the interpreter
    /// indexes. An <see cref="IReadOnlyList{T}"/> indexer is an interface call,
    /// and both are read inside the per-pixel loop to reach an array that was
    /// already an array.
    /// </summary>
    private readonly LoadedSample[] tableArray;

    private readonly LoadedImage[] pictureArray;

    /// <summary>The clips as the IL backend hands them to its methods, for the reason the interpreter indexes them.</summary>
    internal LoadedSample[] TableArray => tableArray;

    /// <summary>The pictures, likewise.</summary>
    internal LoadedImage[] PictureArray => pictureArray;

    private IlProgram? il;

    /// <summary>
    /// This program lowered to IL and put through the JIT, or null while it is
    /// only interpreted — see <see cref="IlCompiler"/>, which is what attaches one.
    /// </summary>
    /// <remarks>
    /// A renderer reads this once per frame or per buffer and runs whichever it
    /// found. Both give the same doubles, so it may change between two reads
    /// without anything being heard or seen — which is what lets it arrive on a
    /// background thread after the program is already playing. It is the one
    /// thing about a compiled patch that changes after it is made, and it is a
    /// faster way to the same answer rather than a different answer.
    /// </remarks>
    public IlProgram? Il => Volatile.Read(ref il);

    /// <summary>
    /// Makes <paramref name="program"/> what renderers run for this patch, or
    /// puts the interpreter back when it is null.
    /// </summary>
    /// <exception cref="ArgumentException">The IL was bound to a different patch, whose constants it would read.</exception>
    public void Attach(IlProgram? program)
    {
        if (program is not null && !ReferenceEquals(program.Source, this))
            throw new ArgumentException("The IL program was bound to a different patch.", nameof(program));

        Volatile.Write(ref il, program);
    }

    private Cue? cue;
    private int holding;

    /// <summary>What this program starts on, or null for one that plays at once.</summary>
    public Cue? Cue => Volatile.Read(ref cue);

    /// <summary>
    /// Whether a renderer should leave this program unplayed, silent and with its
    /// clock standing: it belongs to a patch just opened whose cue has not gone.
    /// </summary>
    public bool Waiting => Cue?.Waiting == true;

    /// <summary>Makes this program start on <paramref name="start"/>, with whatever else is waiting on it.</summary>
    public void WaitFor(Cue start) => Volatile.Write(ref cue, start);

    /// <summary>Takes a part of the cue for this program's own IL, once.</summary>
    internal void Hold()
    {
        if (Cue is { } start && Interlocked.Exchange(ref holding, 1) == 0) start.Take();
    }

    /// <summary>Gives that part back, once.</summary>
    internal void Release()
    {
        if (Cue is { } start && Interlocked.Exchange(ref holding, 0) == 1) start.Give();
    }

    public int RegisterCount { get; }

    /// <summary>
    /// The same ops sorted by how often a frame has to run them, or null for a
    /// program that cannot be sorted. See <see cref="FramePlan"/>. Worked out
    /// here because it is a property of the program, which outlives a frame: the
    /// walk is paid once per edit against every frame drawn in between.
    /// </summary>
    public FramePlan? Plan { get; }

    /// <summary>First of the <see cref="OutputWidth"/> registers holding the result.</summary>
    public int OutputBase { get; }

    /// <summary>3 for a video sink's RGB, 2 for an audio sink's stereo pair.</summary>
    public int OutputWidth { get; }

    /// <summary>
    /// The longest delay each stateful op will ask for, in the order those ops
    /// run. A renderer sizes one ring buffer per entry; a program with none — the
    /// usual case — needs no state at all.
    /// </summary>
    public IReadOnlyList<float> DelayLengths { get; }

    /// <summary>
    /// How many traces this program keeps — one per Scope whose input it
    /// evaluates, which is the speakers' program and no other. Counted from the
    /// highest slot, because a scope wired to nothing emits none and would
    /// otherwise shift every scope after it.
    /// </summary>
    public int TraceCount { get; }

    /// <summary>
    /// How many phase accumulators the program runs. One cell each, so unlike a
    /// delay line there is nothing to size — but a program with any of these
    /// still needs state, and a renderer that gave it none would hand every
    /// oscillator back its multiply.
    /// </summary>
    public int PhaseCount { get; }

    /// <summary>
    /// How many one-evaluation cells the program needs — one per cycle in the
    /// patch it came from. Taken from the highest slot any op names, since a read
    /// and its write share a cell and counting ops would count each cell twice.
    /// </summary>
    public int UnitCount { get; }

    /// <summary>
    /// How many planes the program needs — one per cycle in the patch it came
    /// from. Counted from the highest slot for the reason
    /// <see cref="UnitCount"/> is.
    /// </summary>
    /// <remarks>
    /// The one count that decides megabytes rather than words: the screen keeps a
    /// number per pixel for each of these, where the speakers keep one — see
    /// <see cref="PlaneState"/> and <see cref="OpCode.PlaneRead"/>. Zero for a
    /// patch with no loop in it, which is most of them, and that patch allocates
    /// nothing at all.
    /// </remarks>
    public int PlaneCount { get; }

    /// <summary>
    /// A program whose output is all zeroes — what the compiler falls back to for
    /// a graph with no Output, meaning one assembled by hand rather than through
    /// <see cref="Graph.Patch.EnsureOutput"/>.
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
    /// <param name="feedback">The frame before this one, for <see cref="OpCode.SampleFeedback"/> to read. Empty on the first frame and off the audio path.</param>
    /// <param name="delays">
    /// Memory for the stateful ops, or null on the picture path, where a delay
    /// passes its input straight through.
    /// </param>
    /// <param name="x">Horizontal position, widened by the aspect ratio. Zero on the audio path.</param>
    /// <param name="y">Vertical position, -1 at the bottom to 1 at the top. Zero on the audio path.</param>
    /// <param name="t">Seconds since the patch started.</param>
    /// <param name="registers">Scratch sized by <see cref="RegisterCount"/>.</param>
    /// <param name="aspect">How far <paramref name="x"/> reaches at the frame's edge, for <see cref="OpCode.LoadAspect"/>.</param>
    /// <param name="live">What is played in from outside, or null for all zeros.</param>
    /// <param name="planes">
    /// This pixel's plane cells, or empty for none, where a loop reads zero. The
    /// speakers keep theirs in <paramref name="delays"/>.
    /// </param>
    internal void Evaluate(
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        DelayState? delays = null,
        double aspect = 1d,
        LiveValues? live = null,
        Span<float> planes = default) =>
        Run(Ops, 0, Ops.Length, x, y, t, registers, feedback, delays, aspect, live, planes);

    /// <summary>
    /// Runs only the ops of <paramref name="stage"/>, for a caller drawing a
    /// frame that means to run each stage where it belongs.
    /// </summary>
    /// <remarks>
    /// The three stages together do exactly what one <see cref="Evaluate"/> does,
    /// provided they run in order into the same register bank and the arguments a
    /// stage does not vary with are held still — see <see cref="FramePlan"/>. A
    /// program with no plan runs whole at <see cref="EvaluationStage.Pixel"/>, so
    /// a caller staging its loops gets the right picture either way.
    /// </remarks>
    internal void EvaluateStage(
        EvaluationStage stage,
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        double aspect = 1d,
        LiveValues? live = null,
        Span<float> planes = default)
    {
        if (Plan is not { } plan)
        {
            if (stage is EvaluationStage.Pixel)
                Run(Ops, 0, Ops.Length, x, y, t, registers, feedback, null, aspect, live, planes);

            return;
        }

        var (from, to) = plan.Range(stage);

        Run(plan.Ops, from, to, x, y, t, registers, feedback, null, aspect, live, planes);
    }

    /// <summary>
    /// Walks <paramref name="ops"/> from <paramref name="from"/> to
    /// <paramref name="to"/>, so the staged path can run a third of the program
    /// without a second copy of the switch.
    /// </summary>
    /// <remarks>
    /// Only a whole run may pass <paramref name="delays"/>: which line or cell a
    /// stateful op uses is counted from the start of the program. A staged run
    /// passes none, which is also what lets its ops be reordered at all.
    /// <para>
    /// <paramref name="planes"/> is the exception, and can be passed to either,
    /// because a plane is named by its op rather than counted: a staged run
    /// carries every plane op in the stage a pixel pays for — see
    /// <see cref="FramePlan"/>.
    /// </para>
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
        LiveValues? live,
        Span<float> planes)
    {
        if (registers.Length < RegisterCount)
            throw new ArgumentException(
                $"Register bank holds {registers.Length}, the program needs {RegisterCount}.",
                nameof(registers));

        // The one place the register file is touched without a bounds check. Every
        // index below was checked against RegisterCount when the program was made,
        // and the guard above is the other half of that. Checking per access costs
        // four compares on an Add, which at thirty ops a pixel is most of what
        // this loop does.
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
                case OpCode.Sign: Reg(ref bank, op.Out) = Signum(Reg(ref bank, op.A)); break;
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
                // open: pixels are evaluated in parallel, and there is no
                // "previous evaluation" for one to mean.
                case OpCode.UnitRead:
                    Reg(ref bank, op.Out) = delays?.ReadUnit((int)op.K) ?? 0d;
                    break;

                case OpCode.UnitWrite:
                    delays?.WriteUnit((int)op.K, Reg(ref bank, op.A));
                    break;

                case OpCode.ClockWrite:
                    delays?.WriteClock((int)op.K, Reg(ref bank, op.A));
                    break;

                // The same pair, for a cycle both sinks can carry. The ear's
                // previous evaluation is the sample before and lives in the state
                // beside the delay lines; the eye's is this pixel in the frame
                // before, and is the one number of the plane that belongs to it.
                // Handed neither, a loop reads zero and stays open.
                case OpCode.PlaneRead:
                {
                    var slot = (int)op.K;

                    Reg(ref bank, op.Out) = delays is not null
                        ? delays.ReadPlane(slot)
                        : (uint)slot < (uint)planes.Length ? planes[slot] : 0d;
                    break;
                }

                case OpCode.PlaneWrite:
                {
                    var slot = (int)op.K;
                    var value = Reg(ref bank, op.A);

                    if (delays is not null) delays.WritePlane(slot, value);
                    else if ((uint)slot < (uint)planes.Length) planes[slot] = Bounded(value);

                    break;
                }

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
    /// What makes the unchecked reads in <see cref="Evaluate"/> safe. Only the
    /// fields an op actually reads — <see cref="OpShape"/> says which — since an
    /// op that takes no operand leaves A, B and C at -1, which is the absence of
    /// a register rather than one out of range.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An op names a register the bank does not hold, which is a malformed
    /// program rather than a bad input — caught here, where the message can name
    /// the instruction.
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
    internal static double Guard(double v) => double.IsFinite(v) ? v : 0d;

    /// <summary>
    /// -1, 0 or 1, and 0 for anything that is not a number.
    /// <see cref="Math.Sign(double)"/> raises on a NaN, which is the one answer an
    /// op in this switch may not give.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Signum(double v) => v > 0d ? 1d : v < 0d ? -1d : 0d;

    /// <summary>
    /// Feedback held below one. At exactly one a delay line never decays and at
    /// more than one it doubles every pass, and that damage persists after the
    /// knob is turned back down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Feedback(double v) => double.IsFinite(v) ? Math.Clamp(v, -0.99d, 0.99d) : 0d;

    /// <summary>
    /// What may be put in a plane, which is <see cref="DelayState.WritePlane"/>'s
    /// bound written again for the other sink's float.
    /// </summary>
    /// <remarks>
    /// A cycle drawn as wires carries no gain of its own, so a loop above unity
    /// is easy to draw and this is where it stops. Clamping rather than refusing
    /// leaves a runaway loop pinned at the rails — a white pixel — instead of
    /// turning it into a NaN that spreads.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Bounded(double v) =>
        double.IsFinite(v) ? (float)Math.Clamp(v, -16d, 16d) : 0f;

    /// <summary>
    /// The largest double below 1. For a tiny negative input, <c>v - floor(v)</c>
    /// is mathematically just under 1 but cancels to exactly 1.0 at any finite
    /// precision, and Fract is half-open — Saw and Tile both read it that way.
    /// </summary>
    private const double JustBelowOne = 0.99999999999999989d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Fract(double v)
    {
        var fraction = v - Math.Floor(v);
        return fraction < 1d ? fraction : JustBelowOne;
    }

    // Exact equality is the point in the three guards below: they trap the one
    // divisor that makes the result undefined. A tolerance would be wrong —
    // Divide(1, 1e-20f) is a legitimate 1e20, and an epsilon would flatten it to
    // zero. Guard already handles the overflow.
    // ReSharper disable CompareOfFloatsByEqualityOperator

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Divide(double a, double b) => b == 0d ? 0d : Guard(a / b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Modulo(double a, double b) => b == 0d ? 0d : Guard(a - b * Math.Floor(a / b));

    internal static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge0 == edge1) return x < edge0 ? 0d : 1d;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    // ReSharper restore CompareOfFloatsByEqualityOperator

    internal static void HsvToRgb(double h, double s, double v, Span<double> rgb)
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
    internal static void Sample(in FeedbackFrame frame, double u, double v, Span<double> rgb)
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
