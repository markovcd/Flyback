using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- naming things ------------------------------------------------------

    [Fact]
    public async Task A_module_added_without_a_handle_is_given_one()
    {
        var added = await Call(Bench(), "add_module", """{"type_id":"osc.sine"}""");

        added.Ok.ShouldBeTrue(added.Text);
        added.Text.ShouldContain("sine1");
    }

    [Fact]
    public async Task Two_of_the_same_module_get_different_handles()
    {
        var bench = Bench();

        (await Call(bench, "add_module", """{"type_id":"osc.sine"}""")).Text.ShouldContain("sine1");
        (await Call(bench, "add_module", """{"type_id":"osc.sine"}""")).Text.ShouldContain("sine2");
    }

    [Fact]
    public async Task A_handle_already_in_use_is_refused()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"a"}""");
        var second = await Call(bench, "add_module", """{"type_id":"osc.saw","handle":"a"}""");

        second.Ok.ShouldBeFalse();
        second.Text.ShouldContain("already");
    }

    /// <summary>A handle is the name describe_patch prints, so one the language could not read back is refused.</summary>
    [Theory]
    [InlineData("knob-1")]
    [InlineData("out")]
    [InlineData("in")]
    [InlineData("A3")]
    public async Task A_handle_the_language_cannot_print_is_refused(string handle)
    {
        var bench = Bench();

        var added = await Call(bench, "add_module", $$"""{"type_id":"value","handle":"{{handle}}"}""");

        added.Ok.ShouldBeFalse();
        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }

    [Fact]
    public async Task A_handle_the_model_chose_is_the_name_describe_patch_prints()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"value","handle":"glow"}""");
        await Call(bench, "connect", """{"from":"glow","to":"output1","to_port":"color"}""");

        (await Call(bench, "describe_patch")).Text.ShouldContain("let glow = ");
    }
}
