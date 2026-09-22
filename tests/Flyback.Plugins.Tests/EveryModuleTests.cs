using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Every module the shipped catalog holds, run rather than only lowered.
/// </summary>
/// <remarks>
/// <c>CompilerInvariants</c> does this for the built-in catalog, which is the
/// half a plugin is not in: most of the Form, Voice, Effects and Mastering
/// modules are reached here and by their own tests and nowhere else, and their
/// emit code is where a module indexes past the ports it declared.
/// </remarks>
public class EveryModuleTests
{
    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    public static TheoryData<string> ModuleTypeIds => [.. Catalog.All.Select(d => d.TypeId)];

    [Theory]
    [MemberData(nameof(ModuleTypeIds))]
    public void Every_module_evaluates(string typeId)
    {
        if (typeId == NodeCatalog.OutputTypeId) return;

        var patch = Alone(typeId);

        foreach (var program in (CompiledPatch[])
                 [patch.CompileForVideo(Catalog).Program, patch.CompileForAudio(Catalog).Program])
        {
            var registers = program.AllocateRegisters();
            var delays = Memory(program);

            foreach (var t in (double[])[0d, 0.5d, 61.125d])
            foreach (var y in (double[])[-1d, 0d, 0.75d])
            foreach (var x in (double[])[-1.5d, 0d, 1.5d])
            {
                Should.NotThrow(
                    () => program.Evaluate(x, y, t, registers, default, delays, aspect: 16d / 9d),
                    $"{typeId} at ({x}, {y}, {t})");

                for (var i = 0; i < program.OutputWidth; i++)
                    double.IsNaN(registers[program.OutputBase + i])
                        .ShouldBeFalse($"{typeId} put NaN on output {i} at ({x}, {y}, {t})");
            }
        }
    }

    /// <summary>
    /// The same, stepped the way the audio path steps it, so a module with a
    /// memory is asked what it remembers rather than only what it computes.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleTypeIds))]
    public void Every_module_stays_finite_over_a_run(string typeId)
    {
        if (typeId == NodeCatalog.OutputTypeId) return;

        var program = Alone(typeId).CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();
        var delays = Memory(program);

        for (var i = 0; i < 4000; i++)
        {
            program.Evaluate(0d, 0d, i / (double)GlobalConstants.SampleRate, registers, default, delays);

            for (var w = 0; w < program.OutputWidth; w++)
                double.IsNaN(registers[program.OutputBase + w])
                    .ShouldBeFalse($"{typeId} put NaN on output {w} at sample {i}");
        }
    }

    /// <summary>
    /// A knob reaching from under 100 Hz into the kilohertz swept evenly would
    /// leave everything below a few hundred hertz in the first sliver of its turn.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleTypeIds))]
    public void A_knob_reaching_into_the_kilohertz_sweeps_in_decades(string typeId)
    {
        foreach (var port in Catalog.Require(typeId).Inputs.Where(p => !p.NeedsAWire && p.Min < 100f && p.Max >= 2000f))
            port.Knee.ShouldBeGreaterThan(0f, $"{typeId}.{port.Name} spans {port.Min}..{port.Max} evenly");
    }

    /// <summary>One module, wired to the Output, with nothing else in the patch.</summary>
    private static Patch Alone(string typeId)
    {
        var builder = new PatchBuilder(Catalog);
        var module = builder.Add(typeId, 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        if (Catalog.Require(typeId).Outputs.Count > 0) builder.Wire(module, 0, output, 0);

        return builder.Patch;
    }

    private static DelayState Memory(CompiledPatch program) => new(
        program.DelayLengths,
        GlobalConstants.SampleRate,
        program.PhaseCount,
        program.UnitCount,
        program.TraceCount,
        program.PlaneCount);
}
