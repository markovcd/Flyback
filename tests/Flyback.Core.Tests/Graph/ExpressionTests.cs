using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Expression: a formula over four sockets, typed rather than wired.
/// </summary>
/// <remarks>
/// It stands for the Maths modules its formula names, so the property worth
/// pinning is that it is them — the same program, op for op, as the patch that
/// wires them by hand. The rest is the reading: what it knows, and what it says
/// about what it does not.
/// </remarks>
public class ExpressionTests
{
    private const string Expression = NodeCatalog.ExpressionTypeId;

    [Fact]
    public void It_is_four_sockets_and_a_formula()
    {
        var def = NodeCatalog.BuiltIn.Require(Expression);

        def.Name.ShouldBe("Expression");
        def.Category.ShouldBe(ModuleCategories.Maths);
        def.Inputs.Select(p => p.Name).ShouldBe(["a", "b", "c", "d"]);
        def.Inputs.Select(p => p.Default).ShouldBe([0f, 1f, 0f, 0f]);
        def.Outputs.Select(p => p.Name).ShouldBe(["out"]);

        def.Extras.Single().Fields.Single().ShouldBeOfType<ExtraField.Text>().Key.ShouldBe("formula");
    }

    [Fact]
    public void A_fresh_one_passes_a_through()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(Expression, (0, 0.3f));

        Formula(expression).ShouldBe("a * b + c");
        Heard(b, expression).ShouldBe(0.3, 1e-7);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7d)]
    [InlineData("(1 + 2) * 3", 9d)]
    [InlineData("10 - 4 - 3", 3d)]
    [InlineData("-2 * -a", 1d)]
    [InlineData("-(a + b)", -3.5d)]
    [InlineData("7 % 4", 3d)]
    [InlineData("a*b", 1.5d)]
    [InlineData("pow(b, 3)", 27d)]
    [InlineData("pow(b)", 9d)]
    [InlineData("clamp(b * 10)", 1d)]
    [InlineData("max(a, b) - min(a, b)", 2.5d)]
    [InlineData("step(1, b) + step(1, a)", 1d)]
    [InlineData("remap(a, 0, 1, 10, 20)", 15d)]
    [InlineData("1.5e1 / 3", 5d)]
    [InlineData("  FLOOR( b * 1.5 )  ", 4d)]
    public void It_reads_arithmetic_the_way_it_is_written(string formula, double heard)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(Expression, (0, 0.5f), (1, 3f));
        Formula(expression, formula);

        Heard(b, expression).ShouldBe(heard, 1e-6);
    }

    [Fact]
    public void Pi_and_tau_are_the_numbers_a_knob_set_to_them_holds()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(Expression);
        Formula(expression, "tau - pi");

        Heard(b, expression).ShouldBe(MathF.Tau - MathF.PI);
    }

    /// <summary>
    /// A formula against the modules it names, wired by hand. The programs are
    /// compared op for op — every op the output is computed from, in order, with
    /// what it reads — and then over a frame's worth of pixels with equality
    /// rather than a tolerance: a preset moved onto this module is heard by ear
    /// and looked at by eye, and "the same" has to mean it.
    /// </summary>
    /// <remarks>
    /// Which registers the ops land in is left out. The knobs on the sockets a
    /// formula does not read are given registers before the formula is lowered
    /// and are then swept (ADR-0096), so the numbering starts further along.
    /// </remarks>
    [Theory]
    [InlineData("(floor(a * 45) + 0.5) / 45")]
    [InlineData("a * (b * b * 0.035 + 1)")]
    [InlineData("smoothstep(0.2, 0.8, a) * -b + remap(a, -1, 1, 0, 2)")]
    public void It_is_the_modules_it_spells_op_for_op(string formula)
    {
        var typed = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = typed.Add(NodeCatalog.CoordTypeId);
        var expression = typed.Add(Expression);
        Formula(expression, formula);
        typed.Wire(coord, NodeCatalog.CoordXPort, expression, 0).Wire(coord, NodeCatalog.CoordYPort, expression, 1);
        Shown(typed, expression);

        var wired = new PatchBuilder(NodeCatalog.BuiltIn);
        var x = wired.Add(NodeCatalog.CoordTypeId);
        Shown(wired, formula switch
        {
            "(floor(a * 45) + 0.5) / 45" =>
                Knobbed(wired, "math.div", Knobbed(wired, "math.add", Through(wired, "math.floor", Knobbed(wired, "math.mul", x, 45f)), 0.5f), 45f),

            "a * (b * b * 0.035 + 1)" =>
                Pair(wired, "math.mul", x, Knobbed(wired, "math.add", Knobbed(wired, "math.mul", Pair(wired, "math.mul", x, x, 1, 1), 0.035f), 1f)),

            _ => Pair(wired, "math.add",
                Pair(wired, "math.mul", Ramp(wired, x), Through(wired, "math.neg", x, NodeCatalog.CoordYPort)),
                Remap(wired, x)),
        });

        var byFormula = typed.Patch.CompileForVideo(NodeCatalog.BuiltIn);
        var byHand = wired.Patch.CompileForVideo(NodeCatalog.BuiltIn);

        byFormula.HasErrors.ShouldBeFalse();
        Spelled(byFormula.Program).ShouldBe(Spelled(byHand.Program));
        Drawn(byFormula.Program).ShouldBe(Drawn(byHand.Program));
    }

    [Fact]
    public void A_part_written_twice_is_lowered_once()
    {
        int Ops(string formula)
        {
            var b = new PatchBuilder(NodeCatalog.BuiltIn);
            var coord = b.Add(NodeCatalog.CoordTypeId);
            var expression = b.Add(Expression);
            Formula(expression, formula);
            b.Wire(coord, NodeCatalog.CoordXPort, expression, 0);
            Shown(b, expression);

            return b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops.Length;
        }

        Ops("floor(a * 45) + floor(a * 45)").ShouldBe(Ops("floor(a * 45) + floor(a * 46)") - 3);
    }

    [Theory]
    [InlineData("a +", "it ends where a value was expected, at character 4")]
    [InlineData("a * e", "'e' is not a socket — the sockets are a, b, c and d, at character 5")]
    [InlineData("wobble(a)", "there is no function 'wobble', at character 1")]
    [InlineData("a b", "'b' is not expected here, at character 3")]
    [InlineData("sin", "'sin' is a function, and wants its arguments in brackets, at character 1")]
    [InlineData("pow(a, b, c)", "'pow' takes 2 — a, b, at character 1")]
    [InlineData("(a + b", "it ends where ')' was expected, at character 7")]
    [InlineData("", "there is nothing in it, at character 1")]
    [InlineData("mixer(a)", "there is no function 'mixer', at character 1")]
    public void A_formula_that_does_not_read_gives_nought_and_says_where(string formula, string reason)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(Expression, (0, 0.5f));
        Formula(expression, formula);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(expression, 0, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);

        result.Issues.ShouldHaveSingleItem().Message.ShouldContain(reason);
        result.Issues.Single().Severity.ShouldBe(IssueSeverity.Error);
        result.Issues.Single().NodeId.ShouldBe(expression.Id);

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);
        registers[result.Program.OutputBase].ShouldBe(0d);
    }

    [Fact]
    public void A_color_on_a_socket_is_worked_on_a_channel_at_a_time()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var color = b.Add("color.rgb", (0, 0.2f), (1, 0.4f), (2, 0.6f));
        var expression = b.Add(Expression);
        Formula(expression, "a * 2 - 0.1");
        b.Wire(color, 0, expression, 0);
        Shown(b, expression);

        var result = b.Patch.CompileForVideo(NodeCatalog.BuiltIn);
        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        registers[result.Program.OutputBase].ShouldBe(0.3, 1e-6);
        registers[result.Program.OutputBase + 1].ShouldBe(0.7, 1e-6);
        registers[result.Program.OutputBase + 2].ShouldBe(1.1, 1e-6);
    }

    [Fact]
    public void It_is_called_by_its_formula_until_it_is_named()
    {
        var def = NodeCatalog.BuiltIn.Require(Expression);
        var node = new PatchBuilder(NodeCatalog.BuiltIn).Add(Expression);

        node.Title(def).ShouldBe("a * b + c");

        Formula(node, "  sin(a * tau)  ");
        node.Title(def).ShouldBe("sin(a * tau)");

        Formula(node, "   ");
        node.Title(def).ShouldBe("Expression");

        node.Rename(def, "wobble");
        node.Title(def).ShouldBe("wobble");
    }

    [Fact]
    public void Its_formula_survives_a_save()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(Expression);
        Formula(expression, "fract(a * 3) - 0.5");

        var loaded = PatchIO.Read(PatchIO.ToJson(b.Patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn);

        Formula(loaded.Patch.Find(expression.Id)!).ShouldBe("fract(a * 3) - 0.5");
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>
    /// The ops that are not constants, in order, each with what it reads written
    /// out as the op or the number behind it rather than as a register.
    /// </summary>
    private static List<string> Spelled(CompiledPatch program)
    {
        var written = new Dictionary<int, string>();
        var spelled = new List<string>();

        foreach (var op in program.Ops)
        {
            if (op.Code == OpCode.Const)
            {
                written[op.Out] = op.K.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            string Read(int register) => register < 0 ? "-" : written.GetValueOrDefault(register, "?");

            var line = $"{op.Code}({Read(op.A)}, {Read(op.B)}, {Read(op.C)}, {op.K})";
            written[op.Out] = $"#{spelled.Count}";
            spelled.Add(line);
        }

        return spelled;
    }

    /// <summary>What a program shows across a coarse frame, every channel of every pixel.</summary>
    private static List<double> Drawn(CompiledPatch program)
    {
        var drawn = new List<double>();
        var registers = program.AllocateRegisters();

        for (var y = -1d; y <= 1d; y += 0.0625d)
            for (var x = -1.75d; x <= 1.75d; x += 0.0625d)
            {
                program.Evaluate(x, y, 0d, registers, default);
                for (var channel = 0; channel < 3; channel++) drawn.Add(registers[program.OutputBase + channel]);
            }

        return drawn;
    }

    private static string Formula(NodeInstance node) =>
        node.StateOf("expression")?["formula"]?.GetValue<string>() ?? string.Empty;

    private static void Formula(NodeInstance node, string formula) =>
        node.SetState("expression", new JsonObject { ["formula"] = formula });

    private static NodeInstance Knobbed(PatchBuilder b, string type, NodeInstance a, float by)
    {
        var node = b.Add(type, (1, by));
        b.Wire(a, 0, node, 0);
        return node;
    }

    private static NodeInstance Through(PatchBuilder b, string type, NodeInstance a, int from = 0)
    {
        var node = b.Add(type);
        b.Wire(a, from, node, 0);
        return node;
    }

    private static NodeInstance Pair(PatchBuilder b, string type, NodeInstance a, NodeInstance c, int from = 0, int second = 0)
    {
        var node = b.Add(type);
        b.Wire(a, from, node, 0).Wire(c, second, node, 1);
        return node;
    }

    private static NodeInstance Ramp(PatchBuilder b, NodeInstance x)
    {
        var node = b.Add("math.smoothstep", (0, 0.2f), (1, 0.8f));
        b.Wire(x, 0, node, 2);
        return node;
    }

    private static NodeInstance Remap(PatchBuilder b, NodeInstance x)
    {
        var node = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0f), (4, 2f));
        b.Wire(x, 0, node, 0);
        return node;
    }

    private static void Shown(PatchBuilder b, NodeInstance what)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(what, 0, sink, NodeCatalog.OutputColorPort);
    }

    private static double Heard(PatchBuilder b, NodeInstance what)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(what, 0, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        return registers[result.Program.OutputBase];
    }
}
