using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The one tool that sets whatever a plugin invented, in place of a tool written
/// for each kind (ADR-0055).
/// </summary>
/// <remarks>
/// The module below is the only thing here that knows what a "glide" is. Nothing
/// in the workbench does, which is the property being checked: an assistant can
/// set a value on a kind of state that did not exist when the workbench was
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
        ];
    }

    private static readonly NodeDef Glide = new(
        "test.glide.porta", "Glide", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, node) => [node[0]])
    {
        Extras = [new GlideExtra()],
    };

    private static PatchWorkbench Bench() =>
        new(NodeCatalog.BuiltIn.With(Provider, [Glide]).Catalog, new Patch());

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    private static async Task<PatchWorkbench> WithGlide()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"test.glide.porta","handle":"g1"}""");

        return bench;
    }

    [Fact]
    public async Task A_declared_number_is_set_and_read_back()
    {
        var bench = await WithGlide();

        var set = await Call(bench, "set_extra",
            """{"handle":"g1","extra":"glide","field":"time","value":0.4}""");

        set.Ok.ShouldBeTrue(set.Text);
        set.Text.ShouldContain("time 0.4");

        var node = bench.Snapshot().FirstOf("test.glide.porta").ShouldNotBeNull();
        node.StateOf("glide")!["time"]!.GetValue<float>().ShouldBe(0.4f);
    }

    [Fact]
    public async Task A_declared_toggle_takes_a_boolean()
    {
        var bench = await WithGlide();

        var set = await Call(bench, "set_extra",
            """{"handle":"g1","extra":"glide","field":"legato","value":true}""");

        set.Ok.ShouldBeTrue(set.Text);

        var node = bench.Snapshot().FirstOf("test.glide.porta").ShouldNotBeNull();
        node.StateOf("glide")!["legato"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task A_value_outside_the_range_is_held_to_it_rather_than_refused()
    {
        var bench = await WithGlide();

        await Call(bench, "set_extra", """{"handle":"g1","extra":"glide","field":"time","value":9}""");

        var node = bench.Snapshot().FirstOf("test.glide.porta").ShouldNotBeNull();
        node.StateOf("glide")!["time"]!.GetValue<float>().ShouldBe(1f);
    }

    [Fact]
    public async Task The_wrong_shape_of_value_is_refused_and_says_what_was_wanted()
    {
        var bench = await WithGlide();

        var set = await Call(bench, "set_extra",
            """{"handle":"g1","extra":"glide","field":"legato","value":3}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("true or false");
    }

    [Fact]
    public async Task An_unknown_field_is_refused_and_lists_the_ones_there_are()
    {
        var bench = await WithGlide();

        var set = await Call(bench, "set_extra",
            """{"handle":"g1","extra":"glide","field":"depth","value":1}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("time");
        set.Text.ShouldContain("legato");
    }

    [Fact]
    public async Task An_unknown_extra_is_refused_and_says_what_the_module_carries()
    {
        var bench = await WithGlide();

        var set = await Call(bench, "set_extra",
            """{"handle":"g1","extra":"chord","field":"time","value":1}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("glide");
    }

    /// <summary>
    /// The engine's own three keep their own tools: they store in typed fields
    /// and declare no schema, so there is nothing here for this to set.
    /// </summary>
    [Fact]
    public async Task A_built_in_kind_is_refused_and_points_at_its_own_tool()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.notes","handle":"s1"}""");

        var set = await Call(bench, "set_extra",
            """{"handle":"s1","extra":"notes","field":"value","value":1}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("set_steps");
    }

    /// <summary>
    /// The listing has to name the extra and its fields, or nothing above is
    /// discoverable — a tool that can set anything is useless if the model
    /// cannot find out what there is.
    /// </summary>
    [Fact]
    public async Task The_module_listing_names_what_a_plugins_module_carries()
    {
        var bench = await WithGlide();

        var described = await Call(bench, "describe_module", """{"type_id":"test.glide.porta"}""");

        described.Text.ShouldContain("glide");
        described.Text.ShouldContain("time");
        described.Text.ShouldContain("set_extra");
    }
}
