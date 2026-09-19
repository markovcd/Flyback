using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Chains of Maths modules folded into Expressions, and the program staying what
/// it was: every test compares the two opcode for opcode and register for
/// register, since a preset folded this way is heard and seen as it was.
/// </summary>
public class ExpressionFusionTests
{
    private const string Expression = NodeCatalog.ExpressionTypeId;

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    /// <summary>Folds a copy and checks both programs are the patch's own.</summary>
    private static Patch Fused(Patch patch)
    {
        var video = Fingerprint(patch.CompileForVideo(NodeCatalog.BuiltIn).Program).ToList();
        var audio = Fingerprint(patch.CompileForAudio(NodeCatalog.BuiltIn).Program).ToList();

        var fused = ExpressionFusion.Fuse(patch, NodeCatalog.BuiltIn);

        Fingerprint(fused.CompileForVideo(NodeCatalog.BuiltIn).Program).ShouldBe(video);
        Fingerprint(fused.CompileForAudio(NodeCatalog.BuiltIn).Program).ShouldBe(audio);

        return fused;
    }

    private static string Formula(NodeInstance node) =>
        node.StateOf("expression")?["formula"]?.GetValue<string>() ?? string.Empty;

    private static IEnumerable<NodeInstance> Of(Patch patch, string type) => patch.Nodes.Where(n => n.TypeId == type);

    private static NodeInstance Knobbed(PatchBuilder b, string type, NodeInstance from, float by, int port = 0)
    {
        var node = b.Add(type, (1, by));
        b.Wire(from, port, node, 0);
        return node;
    }

    private static NodeInstance Shown(PatchBuilder b, NodeInstance what)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(what, 0, sink, NodeCatalog.OutputColorPort);
        return sink;
    }

    [Fact]
    public void A_chain_is_one_formula()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var floor = b.Add("math.floor");
        b.Wire(Knobbed(b, "math.add", Knobbed(b, "math.mul", coord, 2f), 0.5f), 0, floor, 0);
        Shown(b, floor);

        var fused = Fused(b.Patch);

        Formula(Of(fused, Expression).ShouldHaveSingleItem()).ShouldBe("floor(a * 2 + 0.5)");
        fused.Nodes.ShouldNotContain(n => n.TypeId.StartsWith("math.", StringComparison.Ordinal) && n.TypeId != Expression);
    }

    /// <summary>The Expression takes the place and the id of the module the chain ended in.</summary>
    [Fact]
    public void The_last_module_of_a_chain_becomes_the_expression()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var last = Knobbed(b, "math.sub", Knobbed(b, "math.mul", coord, 3f), 1f);
        Shown(b, last);

        var fused = Fused(b.Patch);

        fused.Find(last.Id).ShouldNotBeNull().TypeId.ShouldBe(Expression);
    }

    /// <summary>What two modules read is a signal to both, so it folds into neither.</summary>
    [Fact]
    public void A_module_read_twice_stands_on_its_own()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var shared = Knobbed(b, "math.mul", coord, 2f);
        var sum = b.Add("math.add");
        b.Wire(Knobbed(b, "math.sin", shared, 0f), 0, sum, 0).Wire(Knobbed(b, "math.cos", shared, 0f), 0, sum, 1);
        Shown(b, sum);

        var fused = Fused(b.Patch);

        Of(fused, Expression).Select(Formula).Order().ShouldBe(["a * 2", "sin(a) + cos(a)"]);
    }

    /// <summary>A Clamp, a Mix, a Smoothstep and a Remap keep their named knobs.</summary>
    [Fact]
    public void A_module_of_more_than_two_inputs_is_left_alone()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var ramp = b.Add("math.smoothstep", (0, 0.2f), (1, 0.8f));
        b.Wire(Knobbed(b, "math.mul", coord, 2f), 0, ramp, 2);
        Shown(b, ramp);

        var fused = Fused(b.Patch);

        Of(fused, "math.smoothstep").ShouldHaveSingleItem();
        Formula(Of(fused, Expression).ShouldHaveSingleItem()).ShouldBe("a * 2");
    }

    /// <summary>A module a panel knob turns is the knob's to turn, so it stays a module.</summary>
    [Fact]
    public void A_module_on_a_panel_knob_is_left_alone()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var scaled = Knobbed(b, "math.mul", coord, 2f);
        var knob = b.Patch.AddControl("Size", 0.5f);
        ControlMap.Link(scaled, 1, new ControlLink(knob.Id, 0f, 4f));
        Shown(b, Knobbed(b, "math.add", scaled, 1f));

        var fused = Fused(b.Patch);

        Of(fused, "math.mul").ShouldHaveSingleItem();
        Formula(Of(fused, Expression).ShouldHaveSingleItem()).ShouldBe("a + 1");
    }

    /// <summary>Four sockets, so a chain reading a fifth signal leaves that branch to fold on its own.</summary>
    [Fact]
    public void A_fifth_signal_starts_another_expression()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var clock = b.Add(NodeCatalog.TimeTypeId);

        NodeInstance Pair(NodeInstance left, int from, NodeInstance right, int second)
        {
            var node = b.Add("math.add");
            b.Wire(left, from, node, 0).Wire(right, second, node, 1);
            return node;
        }

        var sum = b.Add("math.mul");
        b.Wire(Pair(Pair(coord, 0, coord, 1), 0, Pair(coord, 2, coord, 3), 0), 0, sum, 0).Wire(clock, 0, sum, 1);
        Shown(b, sum);

        var fused = Fused(b.Patch);

        Of(fused, Expression).Select(Formula).Order().ShouldBe(["a * b", "a + b + (c + d)"]);
    }

    /// <summary>A loop reads what its wire carried the evaluation before, which a formula cannot say.</summary>
    [Fact]
    public void A_module_in_a_loop_is_left_alone()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var sum = b.Add("math.add");
        var keep = Knobbed(b, "math.mul", sum, 0.9f);
        b.Wire(clock, 0, sum, 0).Wire(keep, 0, sum, 1);
        Shown(b, sum);

        var fused = Fused(b.Patch);

        Of(fused, Expression).ShouldBeEmpty();
    }

    /// <summary>A formula stops growing where it would no longer read on a line.</summary>
    [Fact]
    public void A_long_chain_is_cut_where_its_formula_would_run_long()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var node = b.Add(NodeCatalog.CoordTypeId);

        for (var i = 0; i < 20; i++) node = Knobbed(b, "math.add", node, 0.123456f);

        Shown(b, node);

        var fused = Fused(b.Patch);

        Of(fused, Expression).Count().ShouldBeGreaterThan(1);
        Of(fused, Expression).ShouldAllBe(n => Formula(n).Length <= 60);
    }

    /// <summary>The engine's presets arrive folded, so folding one again finds nothing left to fold.</summary>
    [Fact]
    public void A_shipped_preset_arrives_folded()
    {
        var band = Presets.All.Single(p => p.Name == "Whole band").Build(NodeCatalog.BuiltIn);
        var count = band.Nodes.Count;

        Of(band, Expression).ShouldNotBeEmpty();
        ExpressionFusion.Fuse(band, NodeCatalog.BuiltIn).Nodes.Count.ShouldBe(count);
    }
}
