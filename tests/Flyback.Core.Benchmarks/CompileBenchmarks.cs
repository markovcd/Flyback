using BenchmarkDotNet.Attributes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Benchmarks;

/// <summary>
/// What an edit costs on the way to machine code: the graph lowered to ops, the
/// ops lowered to IL and put through the JIT, and — the knob case — a program
/// bound to code already built for its shape.
/// </summary>
/// <remarks>
/// ADR-0021 recompiles on every mouse move of a knob drag. <see cref="IlStaged"/>
/// and <see cref="IlWhole"/> are what <see cref="IlCompiler"/> spends on its own
/// thread for an edit that changes the picture's program or the sound's;
/// <see cref="Rebind"/> is what a knob move spends on the UI thread.
/// </remarks>
[MemoryDiagnoser]
public class CompileBenchmarks
{
    private Patch graph = null!;
    private CompiledPatch patch = null!;
    private CompiledPatch[] turned = null!;
    private IlCompiler compiler = null!;
    private int next;

    [Params("Plasma", "Nebula", "WholeBand")]
    public string Preset { get; set; } = "Plasma";

    [GlobalSetup]
    public void Setup()
    {
        graph = Preset switch
        {
            "Nebula" => Presets.Nebula(NodeCatalog.Current),
            "WholeBand" => Presets.WholeBand(NodeCatalog.Current),
            _ => Presets.Plasma(NodeCatalog.Current),
        };

        patch = graph.CompileForVideo().Program;

        // Two programs of one shape, differing in a constant, so each submission
        // is a knob having moved rather than the same program handed in again.
        turned = [Turned(patch, 1f), Turned(patch, 2f)];

        compiler = new IlCompiler();
        compiler.Submit(turned[0], IlLane.Picture);
        compiler.Settled().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => compiler.Dispose();

    [Benchmark(Baseline = true)]
    public CompiledPatch Ops() => graph.CompileForVideo().Program;

    /// <summary>What the picture's lane builds.</summary>
    [Benchmark]
    public IlProgram IlStaged() => IlProgram.Compile(patch, IlParts.Staged);

    /// <summary>What the sound's lane builds, for the same program so the two can be compared.</summary>
    [Benchmark]
    public IlProgram IlWhole() => IlProgram.Compile(patch, IlParts.Whole);

    /// <summary>Both, which no lane asks for — the cost the split avoids.</summary>
    [Benchmark]
    public IlProgram IlAll() => IlProgram.Compile(patch);

    [Benchmark]
    public void Rebind() => compiler.Submit(turned[next++ & 1], IlLane.Picture);

    private static CompiledPatch Turned(CompiledPatch program, float by)
    {
        var ops = program.Ops.ToArray();
        var at = Array.FindLastIndex(ops, o => o.Code is OpCode.Const);
        ops[at] = new Op(OpCode.Const, ops[at].Out, k: ops[at].K + 1000f + by);

        return new CompiledPatch(ops, program.RegisterCount, program.OutputBase, program.OutputWidth);
    }
}
