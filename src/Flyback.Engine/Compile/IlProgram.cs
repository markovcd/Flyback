using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyback.Engine.Compile;

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