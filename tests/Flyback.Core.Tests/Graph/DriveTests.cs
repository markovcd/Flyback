using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>The Drive module: soft saturation, peak-normalised as it goes.</summary>
public class DriveTests
{
    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_shaping_and_is_untyped()
    {
        var def = Modules.Get(NodeCatalog.DriveTypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Drive");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Inputs[0].Kind.ShouldBe(PortKind.Any);
        def.Outputs[0].Kind.ShouldBe(PortKind.Any);
    }

    /// <summary>
    /// What the normalisation is for: the curve is divided by what it does to a
    /// full-scale input, so drive changes the shape of a signal and never the
    /// height of it.
    /// </summary>
    [Fact]
    public void Driving_harder_never_makes_it_louder()
    {
        foreach (var drive in new[] { 0f, 1f, 2f, 8f, 16f })
        {
            Through([1f], 0, (1, drive))[0].ShouldBe(1f, 1e-5f);
            Through([-1f], 0, (1, drive))[0].ShouldBe(-1f, 1e-5f);

            foreach (var sample in Through(Ramp(-1f, 1f, 500), 0, (1, drive)))
                MathF.Abs(sample).ShouldBeLessThanOrEqualTo(1.000001f);
        }
    }

    /// <summary>
    /// And what it does instead: the quiet parts come up while the loud ones stop
    /// moving, which is the same arithmetic a compressor is.
    /// </summary>
    [Fact]
    public void Driving_harder_brings_the_quiet_parts_up()
    {
        var gentle = Through([0.1f], 0, (1, 1f))[0];
        var hard = Through([0.1f], 0, (1, 12f))[0];

        hard.ShouldBeGreaterThan(gentle);
        gentle.ShouldBeGreaterThan(0.1f);
    }

    [Fact]
    public void At_no_drive_it_is_very_nearly_a_wire()
    {
        foreach (var x in new[] { -1f, -0.4f, 0f, 0.25f, 1f })
            Through([x], 0, (1, 0f))[0].ShouldBe(x, 0.02f);
    }

    [Fact]
    public void A_saturator_is_the_same_module_at_both_sinks()
    {
        foreach (var (x, seen) in Painted((1, 6f)))
            seen.ShouldBe(Through([x], 0, (1, 6f))[0], 1e-6f);
    }

    // --- harness ----------------------------------------------------------------

    private static float[] Through(float[] signal, int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.DriveTypeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Modules).Program;
        var registers = program.AllocateRegisters();
        var output = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, i / (double)GlobalConstants.SampleRate, registers, default);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>Compiles the module into the video sink and reads it with no state, as SynthRenderer does.</summary>
    private static (float X, float Seen)[] Painted(params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.DriveTypeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();

        return
        [
            .. new[] { -0.75f, 0f, 0.25f, 0.5f, 1f }.Select(x =>
            {
                program.Evaluate(x, 0f, 0f, registers, default);
                return (x, (float)registers[program.OutputBase]);
            }),
        ];
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Modules.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    private static float[] Ramp(float from, float to, int length) =>
        [.. Enumerable.Range(0, length).Select(i => from + (to - from) * i / (length - 1f))];
}
