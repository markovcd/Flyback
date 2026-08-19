using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// Properties that must hold for every patch the compiler can be handed, rather
/// than for one worked example. The behaviour of individual compiler features
/// (dead code, cycles, port coercion) is specified in the Gherkin suite.
/// </summary>
public class CompilerInvariants
{
    public static TheoryData<string> PresetNames =>
        [.. Presets.All.Select(p => p.Name)];

    public static TheoryData<string> ModuleTypeIds =>
        [.. NodeCatalog.All.Select(d => d.TypeId)];

    /// <summary>
    /// ADR-0021 recompiles the whole patch on every edit — including on every
    /// sample of a slider drag. That is only safe if compilation is a pure
    /// function of the graph, with no accumulated or ambient state.
    /// </summary>
    [Theory]
    [MemberData(nameof(PresetNames))]
    public void Compiling_the_same_patch_twice_yields_an_identical_program(string presetName)
    {
        var patch = BuildPreset(presetName);

        var first = patch.CompileForVideo();
        var second = patch.CompileForVideo();

        Fingerprint(second.Program).ShouldBe(Fingerprint(first.Program));
        second.Program.RegisterCount.ShouldBe(first.Program.RegisterCount);
        second.Program.OutputBase.ShouldBe(first.Program.OutputBase);
    }

    [Theory]
    [MemberData(nameof(PresetNames))]
    public void Every_preset_compiles_without_issues(string presetName)
    {
        var result = BuildPreset(presetName).CompileForVideo();

        // 'Empty' is the blank canvas — a Video Output with nothing in it yet —
        // so the one thing the compiler remarks on is the thing the preset is
        // for. It still has to be a patch nothing is wrong with.
        if (presetName == "Empty")
        {
            result.HasErrors.ShouldBeFalse(
                string.Join(" | ", result.Issues.Select(i => i.Message)));
            return;
        }

        result.Issues.ShouldBeEmpty();
    }

    /// <summary>
    /// Catches the failure mode ADR-0008 warns about: a module's emit function
    /// indexing past the ports it declared. Nothing links the two at compile
    /// time, so every module is exercised at least once here.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleTypeIds))]
    public void Every_module_compiles_when_wired_to_the_output(string typeId)
    {
        if (typeId == NodeCatalog.OutputTypeId) return;

        var def = NodeCatalog.Require(typeId);
        var builder = new PatchBuilder();
        var module = builder.Add(typeId, 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        if (def.Outputs.Count > 0)
            builder.Wire(module, 0, output, 0);

        var result = builder.Patch.CompileForVideo();

        // Nothing wrong rather than nothing said. A module standing on its own
        // has nothing driving its domain, and being told so is correct — what
        // this is checking is that every module lowers without falling over.
        result.HasErrors.ShouldBeFalse(
            string.Join(" | ", result.Issues.Select(i => i.Message)));

        result.Program.Ops.Length.ShouldBeGreaterThan(0);
        result.Program.RegisterCount.ShouldBeGreaterThanOrEqualTo(result.Program.OutputBase + result.Program.OutputWidth);
    }

    /// <summary>
    /// The output color must be three registers that actually exist, or the
    /// renderer reads past the end of its scratch buffer.
    /// </summary>
    [Theory]
    [MemberData(nameof(PresetNames))]
    public void The_output_slot_always_fits_inside_the_register_file(string presetName)
    {
        var program = BuildPreset(presetName).CompileForVideo().Program;

        program.OutputBase.ShouldBeGreaterThanOrEqualTo(0);
        program.RegisterCount.ShouldBeGreaterThanOrEqualTo(program.OutputBase + program.OutputWidth);
        program.AllocateRegisters().Length.ShouldBeGreaterThanOrEqualTo(program.OutputBase + program.OutputWidth);
    }

    /// <summary>
    /// SSA: no op may read a register that no earlier op has written. A
    /// violation would make results depend on leftover scratch from the
    /// previous pixel.
    /// </summary>
    [Theory]
    [MemberData(nameof(PresetNames))]
    public void No_op_reads_a_register_before_it_is_written(string presetName)
    {
        var program = BuildPreset(presetName).CompileForVideo().Program;
        var written = new HashSet<int>();

        foreach (var op in program.Ops)
        {
            foreach (var input in new[] { op.A, op.B, op.C }.Where(r => r >= 0))
                written.ShouldContain(input, $"{op} reads r{input} before anything writes it");

            // Multi-register ops fill Out, Out+1 and Out+2.
            var width = op.Code is OpCode.HsvToRgb or OpCode.SampleFeedback ? 3 : 1;
            for (var i = 0; i < width; i++) written.Add(op.Out + i);
        }
    }

    private static Patch BuildPreset(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));
}
