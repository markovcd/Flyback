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

    /// <summary>
    /// A number a panel knob turns stays on a socket, and the socket follows the
    /// knob: written into the formula, it would stop moving.
    /// </summary>
    [Fact]
    public void A_number_on_a_panel_knob_stays_on_a_socket()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var scaled = Knobbed(b, "math.mul", coord, 2f);
        var knob = b.Patch.AddControl("Size");
        var link = new ControlLink(knob.Id, 0f, 4f);
        ControlMap.Link(scaled, 1, link);
        Shown(b, Knobbed(b, "math.add", scaled, 1f));

        var fused = Fused(b.Patch);

        var expression = Of(fused, Expression).ShouldHaveSingleItem();

        Formula(expression).ShouldBe("a * b + 1");
        expression.InputValues[1].ShouldBe(2f);
        ControlMap.Of(expression, 1).ShouldBe(link);
    }

    /// <summary>
    /// Two numbers either side of an operator stay on sockets: the formula's reader
    /// would add them as floats, where the program adds them in its registers.
    /// </summary>
    [Fact]
    public void Two_numbers_either_side_of_an_operator_stay_on_sockets()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var constant = b.Add("math.add", (0, 0.1f), (1, 0.2f));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(constant, 0, sink, NodeCatalog.OutputLeftPort);

        var fused = Fused(b.Patch);

        var expression = Of(fused, Expression).ShouldHaveSingleItem();

        Formula(expression).ShouldBe("a + b");
        expression.InputValues[0].ShouldBe(0.1f);
        expression.InputValues[1].ShouldBe(0.2f);
    }

    /// <summary>
    /// A negated number stays on a socket too: the reader takes <c>-1</c> for a
    /// number, and would divide it by the one beside it as floats.
    /// </summary>
    [Fact]
    public void A_negated_number_stays_on_a_socket()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var ratio = b.Add("math.div", (1, 1e-7f));
        b.Wire(b.Add("math.neg", (0, 1f)), 0, ratio, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(ratio, 0, sink, NodeCatalog.OutputLeftPort);

        var fused = Fused(b.Patch);

        var expression = Of(fused, Expression).ShouldHaveSingleItem();

        Formula(expression).ShouldBe("-a / 1E-07");
        expression.InputValues[0].ShouldBe(1f);
    }

    /// <summary>A named module is an Expression under its name, which nothing folds into anything else.</summary>
    [Fact]
    public void A_named_module_keeps_its_name()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var scaled = Knobbed(b, "math.mul", coord, 2f);
        scaled.Name = "double";
        Shown(b, Knobbed(b, "math.add", scaled, 1f));

        var fused = Fused(b.Patch);

        Of(fused, Expression).Select(n => (n.Name, Formula(n))).ShouldBe([("double", "a * 2"), (null, "a + 1")], ignoreOrder: true);
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

    /// <summary>
    /// Which wire of a loop carries the evaluation before is the graph's to decide,
    /// so each module on one is an Expression of its own and folds into nothing.
    /// </summary>
    [Fact]
    public void A_module_on_a_loop_stands_on_its_own()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var sum = b.Add("math.add");
        var keep = Knobbed(b, "math.mul", sum, 0.9f);
        b.Wire(clock, 0, sum, 0).Wire(keep, 0, sum, 1);
        Shown(b, sum);

        var fused = Fused(b.Patch);

        Of(fused, Expression).Select(Formula).Order().ShouldBe(["a * 0.9", "a + b"]);
    }

    /// <summary>Expressions fold into one another the way the modules they stand for do.</summary>
    [Fact]
    public void Expressions_fold_into_one_another()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var inner = b.Add(Expression);
        inner.SetState("expression", new System.Text.Json.Nodes.JsonObject { ["formula"] = "a * 60" });
        b.Wire(coord, 0, inner, 0);
        var fract = b.Add("math.fract");
        b.Wire(inner, 0, fract, 0);
        var outer = b.Add(Expression);
        outer.SetState("expression", new System.Text.Json.Nodes.JsonObject { ["formula"] = "a * 2 - 1" });
        b.Wire(fract, 0, outer, 0);
        Shown(b, outer);

        var fused = Fused(b.Patch);

        Formula(Of(fused, Expression).ShouldHaveSingleItem()).ShouldBe("fract(a * 60) * 2 - 1");
    }

    /// <summary>No shipped engine preset keeps a Maths module an Expression stands for.</summary>
    [Fact]
    public void No_shipped_preset_keeps_a_retired_module()
    {
        foreach (var preset in Presets.All)
            preset.Build(NodeCatalog.BuiltIn).Nodes
                .ShouldNotContain(n => ExpressionFusion.Retired(NodeCatalog.BuiltIn.Require(n.TypeId)), preset.Name);
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
