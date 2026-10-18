using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

/// <summary>
/// Whatever a plugin invented is written as named arguments, in place of a notation
/// for each kind (ADR-0055).
/// </summary>
/// <remarks>
/// The module below is the only thing here that knows what a "glide" is. Nothing
/// in the workbench does, which is the property being checked: an assistant can
/// write a value on a kind of state that did not exist when the workbench was
/// written.
/// </remarks>
public class SetExtraTests
{
    private static readonly ModuleProvider Provider = new("test.glide", "Glide modules");

    private sealed record GlideExtra : NodeExtra
    {
        public override string Key => "glide";

        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Number("time", "time", new PortSpec("time", PortKind.Scalar, 0.1f, 0f, 1f)),
            new ExtraField.Toggle("legato", "legato"),
            new ExtraField.Text("label", "label", Multiline: true),
        ];
    }

    private static readonly NodeDef Glide = new(
        "test.glide.porta", "Glide", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (_, node) => [node[0]])
    {
        Extras = [new GlideExtra()],
    };

    private static PatchWorkbench Bench() =>
        new(NodeCatalog.BuiltIn.With(Provider, [Glide]).Catalog, new Patch());

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    private static Task<ToolOutcome> Write(PatchWorkbench bench, string source) =>
        Call(bench, "write_patch", JsonSerializer.Serialize(new { source }));

    private static JsonNode Glided(PatchWorkbench bench) =>
        bench.Snapshot().FirstOf("test.glide.porta").ShouldNotBeNull().StateOf("glide").ShouldNotBeNull();

    [Fact]
    public async Task A_declared_number_is_written_and_read_back()
    {
        var bench = Bench();

        var set = await Write(bench, "let g1 = porta(time: 0.4)");

        set.Ok.ShouldBeTrue(set.Text);
        Glided(bench)["time"]!.GetValue<float>().ShouldBe(0.4f);
    }

    [Fact]
    public async Task A_declared_toggle_is_written_as_one_or_nought()
    {
        var bench = Bench();

        var set = await Write(bench, "let g1 = porta(legato: 1)");

        set.Ok.ShouldBeTrue(set.Text);
        Glided(bench)["legato"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Declared_text_takes_a_string_with_its_line_breaks()
    {
        var bench = Bench();

        var set = await Write(bench, "let g1 = porta(label: \"up|down\")");

        set.Ok.ShouldBeTrue(set.Text);
        Glided(bench)["label"]!.GetValue<string>().ShouldBe("up\ndown");
    }

    [Fact]
    public async Task An_unknown_field_is_refused()
    {
        var set = await Write(Bench(), "let g1 = porta(depth: 1)");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("depth");
    }

    /// <summary>
    /// The listing has to name the extra and its fields, or nothing above is
    /// discoverable: a model cannot write what it cannot find out is there.
    /// </summary>
    [Fact]
    public async Task The_module_listing_names_what_a_plugins_module_carries()
    {
        var described = await Call(Bench(), "describe_module", """{"type_id":"test.glide.porta"}""");

        described.Text.ShouldContain("glide");
        described.Text.ShouldContain("time");
        described.Text.ShouldContain("named arguments");
    }

    /// <summary>An Expression's formula is declared text, so it is a named argument like any other field.</summary>
    [Fact]
    public async Task A_formula_is_text_and_takes_a_string()
    {
        var bench = Bench();

        var set = await Write(bench, "let e1 = expression(formula: \"sin(a * tau) * b\")");

        set.Ok.ShouldBeTrue(set.Text);

        var node = bench.Snapshot().FirstOf("math.expression").ShouldNotBeNull();
        node.StateOf("expression")!["formula"]!.GetValue<string>().ShouldBe("sin(a * tau) * b");
    }
}
