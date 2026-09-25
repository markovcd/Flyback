using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyback.Core.Compile;

/// <summary>
/// A <see cref="CompiledPatch"/> lowered to IL: one method per stretch of the
/// program, each a straight run of the ops with the registers in locals, so the
/// dispatch the interpreter pays per op is gone. See ADR-0076.
/// </summary>
/// <remarks>
/// It computes exactly what <see cref="CompiledPatch.Evaluate"/> computes, to the
/// bit — every op calls the interpreter's own arithmetic through
/// <see cref="IlOps"/>, and the order of the ops is the program's. What it does
/// not carry is the program's constants: those are read from an array bound to
/// one patch, so a patch that differs only in a knob reuses the same machine code.
/// That is the split between <see cref="IlMethods"/>, which is the code and is
/// shared, and this, which is the code bound to one patch.
/// </remarks>
public sealed class IlProgram
{
    private readonly IlContext context;
    private readonly IlStage[]? whole;
    private readonly IlStage[]? frame;
    private readonly IlStage[]? row;
    private readonly IlStage[]? pixel;

    private IlProgram(IlMethods methods, CompiledPatch source)
    {
        Source = source;
        Methods = methods;
        context = IlContext.For(source);

        whole = Bound(methods.Whole);
        frame = Bound(methods.Frame);
        row = Bound(methods.Row);
        pixel = Bound(methods.Pixel);
    }

    private IlStage[]? Bound(DynamicMethod[]? chunks) =>
        chunks is null ? null : [.. chunks.Select(chunk => chunk.CreateDelegate<IlStage>(context))];

    /// <summary>The patch whose constants, clips and pictures this reads.</summary>
    public CompiledPatch Source { get; }

    /// <summary>Which ways of running the program were built. The rest are handed to the interpreter.</summary>
    public IlParts Parts => Methods.Parts;

    internal IlMethods Methods { get; }

    /// <summary>
    /// Lowers <paramref name="patch"/> and puts every method through the JIT before
    /// returning, so the first real call — on a render or audio thread — finds
    /// machine code waiting rather than compiling it there.
    /// </summary>
    /// <remarks>Milliseconds, not microseconds: this is the cost <see cref="IlCompiler"/> exists to keep off an edit.</remarks>
    /// <param name="patch">The program to lower.</param>
    /// <param name="parts">
    /// Which of <see cref="Evaluate"/> and <see cref="EvaluateStage"/> to build. A
    /// renderer calls only one of them, and each part is a separate trip through
    /// the JIT; whatever is not built runs on the interpreter instead.
    /// </param>
    /// <exception cref="NotSupportedException">An op the backend has no lowering for.</exception>
    public static IlProgram Compile(CompiledPatch patch, IlParts parts = IlParts.All) =>
        Bind(IlMethods.Build(patch, parts), patch);

    /// <summary>Code already built for a patch of this shape, bound to this one's constants.</summary>
    internal static IlProgram Bind(IlMethods methods, CompiledPatch patch) => new(methods, patch);

    /// <inheritdoc cref="CompiledPatch.Evaluate"/>
    internal void Evaluate(
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        DelayState? delays = null,
        double aspect = 1d,
        LiveValues? live = null,
        Span<float> planes = default)
    {
        if (whole is null)
        {
            Source.Evaluate(x, y, t, registers, feedback, delays, aspect, live, planes);
            return;
        }

        Check(registers);
        Run(
            whole,
            ref MemoryMarshal.GetReference(registers),
            x, y, t, aspect,
            ref Unsafe.AsRef(in feedback),
            delays, live, planes);
    }

    /// <inheritdoc cref="CompiledPatch.EvaluateStage"/>
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
        var run = stage switch
        {
            EvaluationStage.Frame => frame,
            EvaluationStage.Row => row,
            _ => pixel,
        };

        if (run is null)
        {
            Source.EvaluateStage(stage, x, y, t, registers, feedback, aspect, live, planes);
            return;
        }

        Check(registers);
        Run(
            run,
            ref MemoryMarshal.GetReference(registers),
            x, y, t, aspect,
            ref Unsafe.AsRef(in feedback),
            null, live, planes);
    }

    private static void Run(
        IlStage[] chunks,
        ref double bank,
        double x,
        double y,
        double t,
        double aspect,
        ref FeedbackFrame feedback,
        DelayState? delays,
        LiveValues? live,
        Span<float> planes)
    {
        foreach (var chunk in chunks) chunk(ref bank, x, y, t, aspect, ref feedback, delays, live, planes);
    }

    /// <summary>
    /// Whether this and the interpreter give the same bits over a spread of
    /// coordinates and clocks, whole and staged, with no state.
    /// </summary>
    /// <remarks>
    /// A last check before a build is trusted, cheap enough to run on every one.
    /// It cannot reach what only state reaches — the tests do that — but it does
    /// reach every op's stateless branch, which is most of the program.
    /// </remarks>
    internal bool AgreesWithInterpreter()
    {
        var expected = Source.AllocateRegisters();
        var actual = Source.AllocateRegisters();
        var feedback = default(FeedbackFrame);

        foreach (var t in (ReadOnlySpan<double>)[0d, 0.75d, 61.5d])
        {
            foreach (var y in (ReadOnlySpan<double>)[-0.8d, 0.1d, 0.9d])
            {
                Source.EvaluateStage(EvaluationStage.Frame, 0d, y, t, expected, feedback, 1.5d);
                Source.EvaluateStage(EvaluationStage.Row, 0d, y, t, expected, feedback, 1.5d);
                EvaluateStage(EvaluationStage.Frame, 0d, y, t, actual, feedback, 1.5d);
                EvaluateStage(EvaluationStage.Row, 0d, y, t, actual, feedback, 1.5d);

                foreach (var x in (ReadOnlySpan<double>)[-1.4d, -0.3d, 0.6d, 1.2d])
                {
                    Source.EvaluateStage(EvaluationStage.Pixel, x, y, t, expected, feedback, 1.5d);
                    EvaluateStage(EvaluationStage.Pixel, x, y, t, actual, feedback, 1.5d);
                    if (!SameOutputs(expected, actual)) return false;

                    Source.Evaluate(x, y, t, expected, feedback, aspect: 1.5d);
                    Evaluate(x, y, t, actual, feedback, aspect: 1.5d);
                    if (!SameOutputs(expected, actual)) return false;
                }
            }
        }

        return true;
    }

    private bool SameOutputs(double[] expected, double[] actual)
    {
        for (var c = 0; c < Source.OutputWidth; c++)
        {
            var at = Source.OutputBase + c;
            if (BitConverter.DoubleToInt64Bits(expected[at]) != BitConverter.DoubleToInt64Bits(actual[at]))
                return false;
        }

        return true;
    }

    private void Check(Span<double> registers)
    {
        if (registers.Length < Source.RegisterCount)
            throw new ArgumentException(
                $"Register bank holds {registers.Length}, the program needs {Source.RegisterCount}.",
                nameof(registers));
    }
}

/// <summary>A stretch of a program as one method, taking what the interpreter's walk takes.</summary>
internal delegate void IlStage(
    ref double bank,
    double x,
    double y,
    double t,
    double aspect,
    ref FeedbackFrame feedback,
    DelayState? delays,
    LiveValues? live,
    Span<float> planes);

/// <summary>What an emitted method reads that is neither an argument nor a register: one patch's own values.</summary>
internal sealed class IlContext
{
    public double[] Constants = [];
    public LoadedSample[] Tables = [];
    public LoadedImage[] Pictures = [];

    /// <summary>
    /// Constants are indexed by the register their op writes, so the same code
    /// finds them in any patch of the same shape without a table of where each went.
    /// </summary>
    public static IlContext For(CompiledPatch patch)
    {
        var constants = new double[patch.RegisterCount];

        foreach (var op in patch.Ops)
            if (op.Code is OpCode.Const)
                constants[op.Out] = op.K;

        return new IlContext
        {
            Constants = constants,
            Tables = patch.TableArray,
            Pictures = patch.PictureArray,
        };
    }
}

/// <summary>
/// A program with its constants left out: two patches of one shape run the same
/// machine code, which is what makes turning a knob free for the IL backend.
/// </summary>
/// <remarks>
/// Every other field of every op is part of the shape, <see cref="Op.K"/> included,
/// because outside a constant K is baked into the code — which delay line, which
/// live input, which clip. The frame plan is not, because it is worked out from
/// exactly the fields that are.
/// </remarks>
internal sealed class IlShape : IEquatable<IlShape>
{
    private readonly Op[] ops;
    private readonly int registerCount;
    private readonly int outputBase;
    private readonly int outputWidth;
    private readonly int hash;

    public IlShape(CompiledPatch patch)
    {
        ops = patch.Ops;
        registerCount = patch.RegisterCount;
        outputBase = patch.OutputBase;
        outputWidth = patch.OutputWidth;

        var code = new HashCode();
        code.Add(registerCount);
        code.Add(outputBase);
        code.Add(outputWidth);

        foreach (var op in ops)
        {
            code.Add(op.Code);
            code.Add(op.Out);
            code.Add(op.A);
            code.Add(op.B);
            code.Add(op.C);
            if (op.Code is not OpCode.Const) code.Add(BitConverter.SingleToInt32Bits(op.K));
        }

        hash = code.ToHashCode();
    }

    public bool Equals(IlShape? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (hash != other.hash
            || registerCount != other.registerCount
            || outputBase != other.outputBase
            || outputWidth != other.outputWidth
            || ops.Length != other.ops.Length)
            return false;

        for (var i = 0; i < ops.Length; i++)
        {
            Op a = ops[i], b = other.ops[i];

            if (a.Code != b.Code || a.Out != b.Out || a.A != b.A || a.B != b.B || a.C != b.C) return false;

            if (a.Code is not OpCode.Const
                && BitConverter.SingleToInt32Bits(a.K) != BitConverter.SingleToInt32Bits(b.K))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as IlShape);

    public override int GetHashCode() => hash;
}


/// <summary>Which ways of running a program <see cref="IlProgram"/> builds as machine code.</summary>
[Flags]
public enum IlParts
{
    /// <summary><see cref="IlProgram.Evaluate"/>: the program in the order it was written, which is what the sound runs.</summary>
    Whole = 1,

    /// <summary><see cref="IlProgram.EvaluateStage"/>: the frame, row and pixel shares, which is what the picture runs.</summary>
    Staged = 2,

    All = Whole | Staged,
}

/// <summary>The methods of one shape, compiled to machine code and ready to be bound.</summary>
/// <remarks>Null wherever a part was not asked for, which the bound program hands to the interpreter.</remarks>
internal sealed class IlMethods
{
    private IlMethods(
        IlShape shape,
        IlParts parts,
        DynamicMethod[]? whole,
        DynamicMethod[]? frame,
        DynamicMethod[]? row,
        DynamicMethod[]? pixel)
    {
        Shape = shape;
        Parts = parts;
        Whole = whole;
        Frame = frame;
        Row = row;
        Pixel = pixel;
    }

    public IlShape Shape { get; }

    public IlParts Parts { get; }

    public DynamicMethod[]? Whole { get; }

    public DynamicMethod[]? Frame { get; }

    public DynamicMethod[]? Row { get; }

    public DynamicMethod[]? Pixel { get; }

    public static IlMethods Build(CompiledPatch patch, IlParts parts)
    {
        var outputs = (patch.OutputBase, patch.OutputBase + patch.OutputWidth);

        var whole = parts.HasFlag(IlParts.Whole)
            ? IlEmitter.EmitChunks("Whole", patch.Ops, 0, patch.Ops.Length, outputs)
            : null;

        DynamicMethod[]? frame = null, row = null, pixel = null;

        if (parts.HasFlag(IlParts.Staged))
        {
            if (patch.Plan is { } plan)
            {
                frame = IlEmitter.EmitChunks("Frame", plan.Ops, 0, plan.RowAt, outputs);
                row = IlEmitter.EmitChunks("Row", plan.Ops, plan.RowAt, plan.PixelAt, outputs);
                pixel = IlEmitter.EmitChunks("Pixel", plan.Ops, plan.PixelAt, plan.Ops.Length, outputs);
            }
            else
            {
                // What the interpreter does for a program it cannot sort: the pixel
                // runs all of it, and the other two stages run nothing.
                frame = row = IlEmitter.EmitChunks("Nothing", [], 0, 0, outputs);
                pixel = whole ?? IlEmitter.EmitChunks("Whole", patch.Ops, 0, patch.Ops.Length, outputs);
            }
        }

        var methods = new IlMethods(new IlShape(patch), parts, whole, frame, row, pixel);
        methods.Prepare(patch);
        return methods;
    }

    /// <summary>
    /// Calls each method once, with no state and nothing live, because a dynamic
    /// method is only compiled to machine code when it is first called — and the
    /// first call must not be the audio callback's.
    /// </summary>
    /// <remarks>
    /// The chunks share nothing but read-only constants, so each is called on a
    /// bank of its own and they go through the JIT side by side, on half the cores
    /// and at the caller's priority to leave the audio and render threads theirs.
    /// </remarks>
    private void Prepare(CompiledPatch patch)
    {
        var context = IlContext.For(patch);
        var priority = Thread.CurrentThread.Priority;
        DynamicMethod[] chunks = [.. new[] { Whole, Frame, Row, Pixel }.SelectMany(part => part ?? []).Distinct()];

        Parallel.ForEach(
            chunks,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            chunk =>
            {
                var thread = Thread.CurrentThread;
                var own = thread.Priority;
                thread.Priority = priority;

                try
                {
                    var registers = patch.AllocateRegisters();
                    var feedback = default(FeedbackFrame);
                    chunk.CreateDelegate<IlStage>(context)(
                        ref MemoryMarshal.GetArrayDataReference(registers), 0d, 0d, 0d, 1d, ref feedback, null, null, default);
                }
                finally
                {
                    thread.Priority = own;
                }
            });
    }
}
