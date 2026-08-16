using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The workbench is the whole of what an assistant can do to a patch, and none
/// of it involves a network or a provider — so all of it is tested here, off
/// disk and off the wire.
/// </summary>
/// <remarks>
/// Built against <see cref="NodeCatalog.BuiltIn"/> rather than
/// <see cref="NodeCatalog.Current"/>, so these say the same thing whatever
/// plugins happen to be installed in the test host.
/// </remarks>
public class PatchWorkbenchTests
{
    private static readonly ModuleProvider Extras = new("test.extras", "Extra modules");

    private static readonly NodeDef Doubler = new(
        "test.extras.double", "Double", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, i) => [em.Mul(i[0], 2f)]);

    private static PatchWorkbench Bench(WorkbenchLimits? limits = null, bool vision = true) =>
        new(NodeCatalog.BuiltIn, new Patch(), vision, limits);

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    /// <summary>Builds the smallest patch that renders: one knob into the screen.</summary>
    private static async Task<PatchWorkbench> Lit(float value = 0f)
    {
        var bench = Bench();

        await Call(bench, "add_module", $$"""
            {"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":{{value}}}]}
            """);
        await Call(bench, "add_module", """{"type_id":"video.output","handle":"screen1"}""");
        await Call(bench, "connect", """{"from":"knob1","to":"screen1","to_port":"colour"}""");

        return bench;
    }

    // --- the briefing -------------------------------------------------------

    /// <summary>
    /// The briefing is the cached prefix of every request in a run. A provider
    /// only keeps reading that cache while the bytes are identical, so a
    /// briefing that varies is not a cosmetic problem — it is paying to write
    /// the cache again on every single turn, silently.
    /// </summary>
    [Fact]
    public void The_briefing_is_the_same_text_every_time_it_is_built() =>
        Bench().Briefing.ShouldBe(Bench().Briefing);

    [Fact]
    public void The_briefing_names_every_module_there_is()
    {
        var briefing = Bench().Briefing;

        foreach (var def in NodeCatalog.BuiltIn.All)
            briefing.ShouldContain(def.TypeId);
    }

    [Fact]
    public void The_briefing_carries_what_each_module_is_for()
    {
        // The descriptions are written for a person who does not know the synth,
        // which is the same thing a model needs. Putting them in verbatim is what
        // makes them load-bearing beyond the tooltip they were written for.
        Bench().Briefing.ShouldContain(NodeCatalog.BuiltIn.Require("osc.sine").Description);
    }

    [Fact]
    public void Rendering_is_offered_only_when_the_model_can_see()
    {
        Bench().Tools.Select(t => t.Name).ShouldContain("render");
        Bench(vision: false).Tools.Select(t => t.Name).ShouldNotContain("render");
    }

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

    // --- ports by name ------------------------------------------------------

    /// <summary>
    /// The point of the whole handle-and-name protocol: a model says "value" and
    /// the wire lands on input 2, without anyone counting sockets.
    /// </summary>
    [Fact]
    public async Task A_port_named_in_a_wire_lands_on_the_right_index()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"colour.hsv","handle":"tint1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"tint1","to_port":"value"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var patch = bench.Snapshot();
        var tint = patch.Nodes.Single(n => n.TypeId == "colour.hsv");

        // hue, saturation, value — "value" is the third.
        patch.IncomingTo(tint.Id, 2).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_port_that_does_not_exist_comes_back_with_the_ones_that_do()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"colour.hsv","handle":"tint1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"tint1","to_port":"brightness"}""");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("hue");
        wired.Text.ShouldContain("saturation");
        wired.Text.ShouldContain("value");
    }

    [Fact]
    public async Task A_source_with_one_output_needs_no_port_named()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"video.output","handle":"screen1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"screen1","to_port":"colour"}""");
        wired.Ok.ShouldBeTrue(wired.Text);
    }

    [Fact]
    public async Task A_source_with_several_outputs_is_asked_which_one()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"coord","handle":"coords1"}""");
        await Call(bench, "add_module", """{"type_id":"video.output","handle":"screen1"}""");

        var wired = await Call(bench, "connect", """{"from":"coords1","to":"screen1","to_port":"colour"}""");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("radius");
    }

    // --- what the graph will not allow --------------------------------------

    [Fact]
    public async Task A_second_wire_into_one_input_says_what_it_replaced()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.saw","handle":"saw1"}""");
        await Call(bench, "add_module", """{"type_id":"video.output","handle":"screen1"}""");

        await Call(bench, "connect", """{"from":"sine1","to":"screen1","to_port":"colour"}""");
        var second = await Call(bench, "connect", """{"from":"saw1","to":"screen1","to_port":"colour"}""");

        second.Ok.ShouldBeTrue(second.Text);
        second.Text.ShouldContain("replacing sine1");
        bench.Snapshot().Connections.Count.ShouldBe(1);
    }

    /// <summary>
    /// <see cref="Patch.Connect"/> refuses a self-wire by quietly doing nothing.
    /// Quiet is wrong here: an assistant told nothing happened will try it again.
    /// </summary>
    [Fact]
    public async Task Wiring_a_module_to_itself_is_refused_out_loud()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"add1"}""");
        var wired = await Call(bench, "connect", """{"from":"add1","to":"add1","to_port":"a"}""");

        wired.Ok.ShouldBeFalse();
        wired.Text.ShouldContain("feedback");
    }

    [Fact]
    public async Task A_cycle_is_reported_by_the_next_thing_the_assistant_does()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"add1"}""");
        await Call(bench, "add_module", """{"type_id":"math.add","handle":"add2"}""");
        await Call(bench, "add_module", """{"type_id":"video.output","handle":"screen1"}""");

        await Call(bench, "connect", """{"from":"add2","to":"screen1","to_port":"colour"}""");
        await Call(bench, "connect", """{"from":"add1","to":"add2","to_port":"a"}""");
        var closed = await Call(bench, "connect", """{"from":"add2","to":"add1","to_port":"a"}""");

        closed.Text.ShouldContain("feeds back into itself");
    }

    [Fact]
    public async Task A_patch_with_no_screen_says_so_on_the_first_edit()
    {
        var added = await Call(Bench(), "add_module", """{"type_id":"osc.sine"}""");

        added.Text.ShouldContain("No Video Output");
    }

    // --- unwiring and removing ----------------------------------------------

    [Fact]
    public async Task Unwiring_an_input_puts_it_back_on_its_knob()
    {
        var bench = await Lit(0.25f);

        var cut = await Call(bench, "disconnect", """{"handle":"screen1","port":"colour"}""");

        cut.Ok.ShouldBeTrue(cut.Text);
        bench.Snapshot().Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_module_takes_its_wires_with_it()
    {
        var bench = await Lit();

        var gone = await Call(bench, "remove_module", """{"handle":"knob1"}""");

        gone.Ok.ShouldBeTrue(gone.Text);
        gone.Text.ShouldContain("One wire");

        var patch = bench.Snapshot();
        patch.Nodes.Count.ShouldBe(1);
        patch.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_puts_back_the_patch_that_was_open()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        await Call(bench, "reset");

        bench.Snapshot().Nodes.ShouldBeEmpty();
    }

    // --- proposing ----------------------------------------------------------

    [Fact]
    public async Task A_patch_that_does_not_compile_is_not_worth_proposing()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        var offered = await Call(bench, "propose", """{"summary":"a tone"}""");

        offered.Ok.ShouldBeFalse();
        bench.HasProposal.ShouldBeFalse();
    }

    [Fact]
    public async Task A_clean_patch_can_be_proposed()
    {
        var bench = await Lit(0.5f);

        var offered = await Call(bench, "propose", """{"summary":"a flat grey field"}""");

        offered.Ok.ShouldBeTrue(offered.Text);
        bench.HasProposal.ShouldBeTrue();
        bench.ProposalSummary.ShouldBe("a flat grey field");
    }

    // --- looking ------------------------------------------------------------

    [Fact]
    public async Task Describing_the_patch_names_handles_wires_and_knobs()
    {
        var told = await Call(await Lit(0.25f), "describe_patch");

        told.Text.ShouldContain("knob1 = value");
        told.Text.ShouldContain("screen1 = video.output");
        told.Text.ShouldContain("colour <- knob1.out");
        told.Text.ShouldContain("0.25");
    }

    [Fact]
    public async Task A_render_is_a_png_of_the_size_it_says()
    {
        var looked = await Call(await Lit(0.5f), "render", """{"times":[0.5,1.5]}""");

        looked.Ok.ShouldBeTrue(looked.Text);
        looked.Png.ShouldNotBeNull();

        Signature(looked.Png).ShouldBe("PNG");
        Width(looked.Png).ShouldBe(320 * 2);
        Height(looked.Png).ShouldBe(180);
    }

    /// <summary>
    /// The regression this exists for: the renderer owns the history that
    /// <c>feedback</c> reads, so a render that jumped straight to its target time
    /// would hand back the same black frame whatever time was asked for — and an
    /// assistant shown black would go and "fix" a patch that was working.
    /// </summary>
    [Fact]
    public async Task A_feedback_patch_looks_different_once_it_has_been_warmed()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"feedback","handle":"previous1"}""");
        await Call(bench, "add_module", """
            {"type_id":"colour.gain","handle":"gain1","knobs":[{"port":"gain","value":1},{"port":"bias","value":0.1}]}
            """);
        await Call(bench, "add_module", """{"type_id":"video.output","handle":"screen1"}""");

        await Call(bench, "connect", """{"from":"previous1","to":"gain1","to_port":"colour"}""");
        var wired = await Call(bench, "connect", """{"from":"gain1","to":"screen1","to_port":"colour"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var cold = await Call(bench, "render", """{"times":[0]}""");
        var warm = await Call(bench, "render", """{"times":[1.5]}""");

        cold.Png.ShouldNotBeNull();
        warm.Png.ShouldNotBeNull();
        warm.Png.ShouldNotBe(cold.Png);
    }

    [Fact]
    public async Task A_patch_that_does_not_compile_cannot_be_looked_at()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        var looked = await Call(bench, "render");

        looked.Ok.ShouldBeFalse();
        looked.Png.ShouldBeNull();
    }

    // --- what comes out -----------------------------------------------------

    [Fact]
    public async Task A_snapshot_is_laid_out_left_to_right_with_the_screen_last()
    {
        var patch = (await Lit()).Snapshot();

        var knob = patch.Nodes.Single(n => n.TypeId == "value");
        var screen = patch.Nodes.Single(n => n.TypeId == NodeCatalog.VideoOutputTypeId);

        screen.X.ShouldBeGreaterThan(knob.X);
    }

    [Fact]
    public async Task A_snapshot_is_a_copy_that_the_workbench_cannot_reach()
    {
        var bench = await Lit();
        var first = bench.Snapshot();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");

        first.Nodes.Count.ShouldBe(2);
        bench.Snapshot().Nodes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_snapshot_records_the_plugin_a_module_came_from()
    {
        var catalog = NodeCatalog.BuiltIn.With(Extras, [Doubler]).Catalog;
        var bench = new PatchWorkbench(catalog, new Patch());

        await Call(bench, "add_module", """{"type_id":"test.extras.double"}""");

        bench.Snapshot().Requires.ShouldHaveSingleItem().ShouldBe(Extras);
    }

    // --- refusing rather than throwing --------------------------------------

    [Theory]
    [InlineData("add_module", """{"type_id":"nowhere.at.all"}""")]
    [InlineData("add_module", "{}")]
    [InlineData("set_knobs", """{"handle":"nothing","knobs":[]}""")]
    [InlineData("connect", """{"from":"nothing","to":"nothing","to_port":"x"}""")]
    [InlineData("disconnect", """{"handle":"nothing","port":"x"}""")]
    [InlineData("remove_module", "{}")]
    [InlineData("propose", "{}")]
    [InlineData("nonsense", "{}")]
    public async Task Anything_it_cannot_do_is_refused_rather_than_thrown(string tool, string arguments)
    {
        var outcome = await Call(Bench(), tool, arguments);

        outcome.Ok.ShouldBeFalse();
        outcome.Text.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_knob_that_is_not_a_number_is_refused()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"value","handle":"knob1"}""");
        var set = await Call(bench, "set_knobs", """{"handle":"knob1","knobs":[{"port":"value","value":"loud"}]}""");

        set.Ok.ShouldBeFalse();
    }

    // --- caps ---------------------------------------------------------------

    [Fact]
    public async Task Running_out_of_room_for_modules_is_said_rather_than_thrown()
    {
        var bench = Bench(new WorkbenchLimits(MaxNodes: 1));

        (await Call(bench, "add_module", """{"type_id":"osc.sine"}""")).Ok.ShouldBeTrue();

        var second = await Call(bench, "add_module", """{"type_id":"osc.saw"}""");
        second.Ok.ShouldBeFalse();
        second.Text.ShouldContain("1 modules");
    }

    [Fact]
    public async Task Running_out_of_tool_calls_is_said_rather_than_thrown()
    {
        var bench = Bench(new WorkbenchLimits(MaxToolCalls: 2));

        await Call(bench, "describe_patch");
        await Call(bench, "describe_patch");

        var third = await Call(bench, "describe_patch");
        third.Ok.ShouldBeFalse();
        third.Text.ShouldContain("tool calls");
    }

    // --- reading a png without decoding one ---------------------------------

    private static string Signature(byte[] png) => System.Text.Encoding.ASCII.GetString(png, 1, 3);

    private static int Width(byte[] png) => BigEndian(png, 16);

    private static int Height(byte[] png) => BigEndian(png, 20);

    private static int BigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
