using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// An Expression printed as the arithmetic it is, where that reads back as the
/// same module — and as the call it is everywhere else.
/// </summary>
/// <remarks>
/// Held to what every printing is held to: read back, it compiles to the same
/// program, opcode for opcode and register for register.
/// </remarks>
public class InfixPrinterTests
{
    private static Patch Build(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty($"{load.Report}{Environment.NewLine}{source}");

        return load.Patch;
    }

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    private static void SameInstrument(Patch original, Patch again, string printed)
    {
        Fingerprint(again.CompileForVideo(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(original.CompileForVideo(NodeCatalog.BuiltIn).Program), printed);

        Fingerprint(again.CompileForAudio(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(original.CompileForAudio(NodeCatalog.BuiltIn).Program), printed);

        again.Nodes.Count.ShouldBe(original.Nodes.Count, printed);
    }

    private static int Expressions(Patch patch) => patch.Nodes.Count(n => n.TypeId == NodeCatalog.ExpressionTypeId);

    private static NodeInstance Expression(PatchBuilder b, string formula, params NodeInstance[] sockets)
    {
        var node = b.Add(NodeCatalog.ExpressionTypeId);
        node.SetState("expression", new JsonObject { ["formula"] = formula });

        for (var socket = 0; socket < sockets.Length; socket++) b.Wire(sockets[socket], 0, node, socket);

        return node;
    }

    [Theory]
    [InlineData("(x * 2 - 1) * (y + 0.5) - -x / 3 |> out.color")]
    [InlineData("let across = (fract(t * 60) * 2 - 1) * aspect\nlet down = 1 - fract(t * 0.5) * 2\ncolor.hsv(hue: across * down + t * 0.1) |> out.color")]
    [InlineData("(x + y + t + radius) * angle |> out.color")]
    [InlineData("sine(freq: 220) * 0.5 |> out.left")]
    [InlineData("x % 1 / 12 - (y - t) |> out.color")]
    [InlineData("let wave = sine(freq: 3)\nwave * wave * x |> out.color")]
    public void A_printed_sum_reads_back_as_the_same_instrument(string source)
    {
        var original = Build(source);
        var printed = PatchPrinter.Print(original, NodeCatalog.BuiltIn);

        SameInstrument(original, Build(printed), printed);
        PatchPrinter.Print(Build(printed), NodeCatalog.BuiltIn).ShouldBe(printed);
    }

    /// <summary>
    /// A line that opens on a minus carries on the line above, so a statement
    /// never opens on one.
    /// </summary>
    [Theory]
    [InlineData("let wave = sine(freq: 3)\n(-wave) |> out.color\nwave |> out.left", "(-wave) |> out.color")]
    [InlineData("let wave = sine(freq: 3)\n(-wave) |> clamp() |> out.color\nwave |> out.left", "(-wave) |> clamp() |> out.color")]
    public void A_sum_opening_a_statement_on_a_minus_is_bracketed(string source, string line)
    {
        var original = Build(source);
        var printed = PatchPrinter.Print(original, NodeCatalog.BuiltIn);

        printed.ShouldContain(line);
        SameInstrument(original, Build(printed), printed);
    }

    [Fact]
    public void A_sum_is_printed_as_the_sum()
    {
        var printed = PatchPrinter.Print(Build("(x * 2 - 1) * (y + 0.5) - -x / 3 |> out.color"), NodeCatalog.BuiltIn);

        printed.ShouldBe("(x * 2 - 1) * (y + 0.5) - -x / 3 |> out.color\n");
    }

    /// <summary>A formula with a function in it has no arithmetic to be written as, so it is the call.</summary>
    [Fact]
    public void A_formula_calling_a_function_is_printed_as_a_call()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var steps = Expression(b, "floor(a * 8) / 8", coord);
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(steps, 0, sink, NodeCatalog.OutputColorPort);

        var printed = PatchPrinter.Print(b.Patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("expression(a: _, formula: \"floor(a * 8) / 8\")");
        SameInstrument(b.Patch, Build(printed), printed);
    }

    /// <summary>
    /// A source written out in full would read back as two modules if it stood
    /// twice, so a sum reading one twice is the call.
    /// </summary>
    [Fact]
    public void A_source_written_in_full_is_not_written_twice()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = b.Add("osc.sine", (1, 3f));
        var squared = Expression(b, "a * a", sine);
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(squared, 0, sink, NodeCatalog.OutputColorPort);

        var printed = PatchPrinter.Print(b.Patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("expression(a: _, formula: \"a * a\")");
        SameInstrument(b.Patch, Build(printed), printed);
    }

    /// <summary>
    /// One sum written into another would read back as one Expression, so the
    /// inner is piped into the outer's call instead.
    /// </summary>
    [Fact]
    public void An_expression_into_an_expression_stays_two()
    {
        var original = Build("(x + y + t + radius) * angle |> out.color");
        var printed = PatchPrinter.Print(original, NodeCatalog.BuiltIn);

        Expressions(Build(printed)).ShouldBe(2);
        printed.ShouldContain("x + y + t + radius |> expression(");
    }

    /// <summary>A socket resting on its knob would read back as a number in the formula, so it is the call.</summary>
    [Fact]
    public void A_socket_on_its_knob_is_printed_as_a_call()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var scaled = Expression(b, "a * b", coord);
        scaled.InputValues[1] = 0.25f;
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(scaled, 0, sink, NodeCatalog.OutputColorPort);

        var printed = PatchPrinter.Print(b.Patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("expression(a: _, b: 0.25, formula: \"a * b\")");
        SameInstrument(b.Patch, Build(printed), printed);
    }

    /// <summary>The module a sum places is where its operator is, in a printing as in typed text.</summary>
    [Fact]
    public void The_text_points_at_a_sum_from_its_operator()
    {
        var patch = Build("x * 2 - 1 |> out.color");
        var expression = patch.Nodes.Single(n => n.TypeId == NodeCatalog.ExpressionTypeId);

        var printing = PatchPrinter.Written(patch);

        printing.Source.ShouldStartWith("x * 2 - 1");
        printing.Map.Where(expression.Id).ShouldNotBeNull();
        printing.Map.At(printing.Source.IndexOf('-')).ShouldBe(expression.Id);

        var typed = PatchLanguage.Build("x * 2 - 1 |> out.color", NodeCatalog.BuiltIn);
        var placed = typed.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.ExpressionTypeId);

        typed.Map.At("x * 2 - 1".IndexOf('-')).ShouldBe(placed.Id);
    }
}
