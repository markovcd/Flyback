using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// Only what reaches a sink is compiled for it (ADR-0011, ADR-0022): a module left
/// lying unwired, the other sink's modules and a module switched off cost nothing,
/// and a module read by several sockets is worked out once.
/// </summary>
public class ReachTests
{
    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    private static void Wire(PatchBuilder b, NodeInstance source, string output, NodeInstance target, string input) =>
        b.Wire(source, Port(source, output, outputs: true), target, Port(target, input, outputs: false));

    private static int Port(NodeInstance node, string name, bool outputs)
    {
        var def = Modules.Require(node.TypeId);

        return (outputs ? def.Outputs : def.Inputs).ToList().FindIndex(port => port.Name == name);
    }

    private static int Count(CompileResult compiled, OpCode code) =>
        compiled.Program.Ops.Count(op => op.Code == code);

    [Fact]
    public void A_module_left_unwired_compiles_to_nothing()
    {
        var b = new PatchBuilder(Modules);

        var coords = b.Add("coord");
        b.Add("pattern.clouds");
        Wire(b, coords, "x", b.Add(NodeCatalog.OutputTypeId), "color");

        var compiled = b.Patch.CompileForVideo(Modules);

        compiled.Issues.ShouldBeEmpty();
        Count(compiled, OpCode.Noise3).ShouldBe(0);
    }

    /// <summary>Per module, not per socket: reaching Coordinates at all computes it once, however many inputs read it.</summary>
    [Fact]
    public void A_module_feeding_several_inputs_is_worked_out_once()
    {
        var b = new PatchBuilder(Modules);

        var coords = b.Add("coord");
        var tint = b.Add("color.hsv");

        Wire(b, coords, "x", tint, "hue");
        Wire(b, coords, "x", tint, "saturation");
        Wire(b, coords, "x", tint, "value");
        Wire(b, tint, "color", b.Add(NodeCatalog.OutputTypeId), "color");

        Count(b.Patch.CompileForVideo(Modules), OpCode.LoadX).ShouldBe(1);
    }

    [Fact]
    public void The_picture_and_the_sound_each_compute_only_their_own_modules()
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId);
        var clouds = b.Add("pattern.clouds");
        var tone = b.Add("osc.sine");

        Wire(b, b.Add("coord"), "x", clouds, "x");
        Wire(b, b.Add("time"), "t", tone, "in");
        Wire(b, clouds, "out", output, "color");
        Wire(b, tone, "out", output, "left");

        var picture = b.Patch.CompileForVideo(Modules);
        var sound = b.Patch.CompileForAudio(Modules);

        Count(picture, OpCode.Noise3).ShouldBeGreaterThan(0, "the picture itself is missing");
        Count(picture, OpCode.Sin).ShouldBe(0);

        Count(sound, OpCode.Sin).ShouldBeGreaterThan(0, "the tone itself is missing");
        Count(sound, OpCode.Noise3).ShouldBe(0);
    }

    [Fact]
    public void A_module_that_is_off_compiles_to_nothing()
    {
        var b = new PatchBuilder(Modules);

        var halve = b.Add("math.mul");
        halve.Off = true;

        Wire(b, b.Add("coord"), "x", halve, "a");
        Wire(b, halve, "out", b.Add(NodeCatalog.OutputTypeId), "color");

        Count(b.Patch.CompileForVideo(Modules), OpCode.Mul).ShouldBe(0);
    }
}
