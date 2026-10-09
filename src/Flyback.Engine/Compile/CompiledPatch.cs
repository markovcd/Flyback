using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

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
            [.. ops.Where(o => OpShape.Owns(o.Code) == OpMemory.Line).Select(o => o.K)];
        TraceCount = ops
            .Where(o => o.Code is OpCode.Tap)
            .Select(o => (int)o.K + 1)
            .DefaultIfEmpty(0)
            .Max();
        PhaseCount = ops.Count(o => OpShape.Owns(o.Code) == OpMemory.Cell);
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
        PictureArray = pictures as LoadedImage[] ?? [.. pictures ?? []];
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
    /// a clip is the same for every evaluation and every renderer, and the shader
    /// is handed each as a float texture.
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
    public IReadOnlyList<LoadedImage> Pictures => PictureArray;


    /// <summary>
    /// <see cref="Tables"/> and <see cref="Pictures"/> as what the interpreter
    /// indexes. An <see cref="IReadOnlyList{T}"/> indexer is an interface call,
    /// and both are read inside the per-pixel loop to reach an array that was
    /// already an array.
    /// </summary>
    private readonly LoadedSample[] tableArray;

    /// <summary>The clips as the IL backend hands them to its methods, for the reason the interpreter indexes them.</summary>
    internal LoadedSample[] TableArray => tableArray;

    /// <summary>The pictures, likewise.</summary>
    internal LoadedImage[] PictureArray { get; }

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
    /// <see cref="Flyback.Core.Graph.Patch.EnsureOutput"/>.
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
        Interpreter.Run(this, Ops, 0, Ops.Length, x, y, t, registers, feedback, delays, aspect, live, planes);

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
                Interpreter.Run(this, Ops, 0, Ops.Length, x, y, t, registers, feedback, null, aspect, live, planes);

            return;
        }

        var (from, to) = plan.Range(stage);

        Interpreter.Run(this, plan.Ops, from, to, x, y, t, registers, feedback, null, aspect, live, planes);
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

            foreach (var register in OpShape.Reads(op))
                if (!Holds(register, 1)) throw Malformed(i, op, register, 1);

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
}
