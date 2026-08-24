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

    private static PatchWorkbench Bench(
        WorkbenchLimits? limits = null,
        bool vision = true,
        bool hearing = true) =>
        new(NodeCatalog.BuiltIn, new Patch(), vision, hearing, limits);

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    /// <summary>Builds the smallest patch that renders: one knob into the screen.</summary>
    private static async Task<PatchWorkbench> Lit(float value = 0f)
    {
        var bench = Bench();

        await Call(bench, "add_module", $$"""
            {"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":{{value}}}]}
            """);
        await Call(bench, "connect", """{"from":"knob1","to":"output1","to_port":"color"}""");

        return bench;
    }

    /// <summary>
    /// Builds the smallest patch that actually sounds, and has no screen at
    /// all. The clock is not decoration: an oscillator accumulates how far its
    /// 'in' moved, so one without it is silent however its freq is set.
    /// </summary>
    private static async Task<PatchWorkbench> Heard()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"time","handle":"clock1"}""");
        await Call(bench, "add_module", """
            {"type_id":"osc.sine","handle":"tone1","knobs":[{"port":"freq","value":440}]}
            """);
        await Call(bench, "connect", """{"from":"clock1","to":"tone1","to_port":"in"}""");
        await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"left"}""");

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
        await Call(bench, "add_module", """{"type_id":"color.hsv","handle":"tint1"}""");

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"tint1","to_port":"value"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var patch = bench.Snapshot();
        var tint = patch.Nodes.Single(n => n.TypeId == "color.hsv");

        // hue, saturation, value — "value" is the third.
        patch.IncomingTo(tint.Id, 2).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_port_that_does_not_exist_comes_back_with_the_ones_that_do()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"color.hsv","handle":"tint1"}""");

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

        var wired = await Call(bench, "connect", """{"from":"sine1","to":"output1","to_port":"color"}""");
        wired.Ok.ShouldBeTrue(wired.Text);
    }

    [Fact]
    public async Task A_source_with_several_outputs_is_asked_which_one()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"coord","handle":"coords1"}""");

        var wired = await Call(bench, "connect", """{"from":"coords1","to":"output1","to_port":"color"}""");

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

        await Call(bench, "connect", """{"from":"sine1","to":"output1","to_port":"color"}""");
        var second = await Call(bench, "connect", """{"from":"saw1","to":"output1","to_port":"color"}""");

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

        await Call(bench, "connect", """{"from":"add2","to":"output1","to_port":"color"}""");
        await Call(bench, "connect", """{"from":"add1","to":"add2","to_port":"a"}""");
        var closed = await Call(bench, "connect", """{"from":"add2","to":"add1","to_port":"a"}""");

        closed.Text.ShouldContain("feeds back into itself");
    }

    [Fact]
    public async Task A_patch_that_reaches_nothing_says_so_on_the_first_edit()
    {
        var added = await Call(Bench(), "add_module", """{"type_id":"osc.sine"}""");

        added.Text.ShouldContain("Nothing is wired into the Output");
    }

    /// <summary>
    /// The refusal names the sink that is already there, because the useful next
    /// move is to wire into it — an assistant told only "no" removes the one it
    /// has and adds it again.
    /// </summary>
    [Fact]
    public async Task Adding_the_output_is_refused_and_the_one_already_there_named()
    {
        var bench = await Lit();

        var again = await Call(bench, "add_module", """{"type_id":"output","handle":"output2"}""");

        again.Ok.ShouldBeFalse();
        again.Text.ShouldContain("output1");
        bench.Snapshot().Nodes.Count(n => n.TypeId == NodeCatalog.OutputTypeId).ShouldBe(1);
    }

    /// <summary>
    /// The refusal names both sockets, because an assistant that wanted "an
    /// audio output" needs telling that what it is after is a socket on the
    /// block already in front of it.
    /// </summary>
    [Fact]
    public async Task The_refusal_says_which_sockets_to_use_instead()
    {
        var again = await Call(await Lit(), "add_module", """{"type_id":"output"}""");

        again.Text.ShouldContain("color");
        again.Text.ShouldContain("left");
    }

    [Fact]
    public async Task Every_bench_starts_with_its_output_already_placed()
    {
        var bench = Bench();

        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }

    [Fact]
    public async Task A_refused_sink_leaves_the_patch_as_it_was()
    {
        var bench = await Lit();
        var edits = bench.Edits;

        await Call(bench, "add_module", """{"type_id":"output"}""");

        // A refusal that had half-placed the module would leave a handle
        // reserved for a node that is not there.
        bench.Edits.ShouldBe(edits);
        bench.Snapshot().Nodes.Count.ShouldBe(2);
    }

    // --- unwiring and removing ----------------------------------------------

    [Fact]
    public async Task Unwiring_an_input_puts_it_back_on_its_knob()
    {
        var bench = await Lit(0.25f);

        var cut = await Call(bench, "disconnect", """{"handle":"output1","port":"color"}""");

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

        // Back to what was open, which is never nothing: the Output survives a
        // reset because every patch has one.
        bench.Snapshot().Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);
    }

    // --- proposing ----------------------------------------------------------

    [Fact]
    public async Task A_patch_that_does_not_compile_is_not_worth_proposing()
    {
        var bench = Bench();

        // A cycle the screen can reach. The tools allow one to be built — the
        // compiler is what refuses it — so this is how a genuine fault gets in.
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"sine2"}""");
        await Call(bench, "connect", """{"from":"sine1","to":"sine2","to_port":"in"}""");
        await Call(bench, "connect", """{"from":"sine2","to":"sine1","to_port":"in"}""");
        await Call(bench, "connect", """{"from":"sine1","to":"output1","to_port":"color"}""");

        var offered = await Call(bench, "propose", """{"summary":"a tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("feeds back into itself");
        bench.HasProposal.ShouldBeFalse();
    }

    /// <summary>
    /// A patch built for the speakers alone is a patch somebody meant, and may
    /// be offered as it stands. Blocking it on the screen it deliberately does
    /// not have left an Apply button that could never light up.
    /// </summary>
    [Fact]
    public async Task A_patch_built_for_the_speakers_alone_can_be_proposed()
    {
        var bench = await Heard();

        var offered = await Call(bench, "propose", """{"summary":"a 440 hz tone"}""");

        offered.Ok.ShouldBeTrue(offered.Text);
        bench.HasProposal.ShouldBeTrue();
        bench.ProposalSummary.ShouldBe("a 440 hz tone");
    }

    /// <summary>
    /// Now that the audio branch can be the whole point of a patch, it has to be
    /// compiled before one is offered. The video pass never reaches a node only
    /// the ear does, so on its own it would have said the patch was fine.
    /// </summary>
    [Fact]
    public async Task A_fault_only_the_speakers_reach_still_stops_a_proposal()
    {
        var bench = await Heard();

        await Call(bench, "add_module", """{"type_id":"osc.saw","handle":"saw1"}""");
        await Call(bench, "connect", """{"from":"saw1","to":"tone1","to_port":"in"}""");
        await Call(bench, "connect", """{"from":"tone1","to":"saw1","to_port":"in"}""");

        var offered = await Call(bench, "propose", """{"summary":"a 440 hz tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("feeds back into itself");
        bench.HasProposal.ShouldBeFalse();
    }

    [Fact]
    public async Task A_patch_that_reaches_neither_the_screen_nor_the_speakers_is_not_proposed()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        var offered = await Call(bench, "propose", """{"summary":"a tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("nothing is wired into the Output");
        bench.HasProposal.ShouldBeFalse();
    }

    /// <summary>
    /// An assistant reads this after every edit. A patch built for the speakers
    /// used to trip the missing-screen warning on every one of them, and one
    /// that reads that as something to fix spends the run fixing it.
    /// </summary>
    [Fact]
    public async Task A_patch_built_for_the_speakers_is_not_nagged_about_the_screen()
    {
        var bench = await Heard();

        var told = await Call(bench, "describe_patch");

        told.Text.ShouldNotContain("no output");
        (await Call(bench, "set_knobs", """
            {"handle":"output1","knobs":[{"port":"gain","value":0.8}]}
            """)).Text.ShouldContain("No issues.");
    }

    /// <summary>
    /// The moment anything reaches the sink the complaint goes, which is what
    /// stops it being noise for the whole of the rest of the run. There is no
    /// longer a complaint about *having* no output — the block is always there,
    /// so the only thing left to say is that nothing arrives at it.
    /// </summary>
    [Fact]
    public async Task Wiring_the_output_settles_the_complaint_about_reaching_nothing()
    {
        var bench = Bench();

        (await Call(bench, "add_module", """{"type_id":"value","handle":"knob1"}"""))
            .Text.ShouldContain("Nothing is wired into the Output");

        (await Call(bench, "connect", """{"from":"knob1","to":"output1","to_port":"color"}"""))
            .Text.ShouldNotContain("Nothing is wired into the Output");
    }

    // --- a sequencer's notes ------------------------------------------------

    /// <summary>
    /// The notes are a list on the module rather than knobs (ADR-0038), so
    /// set_knobs cannot reach them and this is the only way to write a tune.
    /// </summary>
    [Fact]
    public async Task A_tune_can_be_written_in_one_call()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.notes","handle":"tune1"}""");

        var set = await Call(bench, "set_steps", """
            {"handle":"tune1","notes":[{"value":60},{"value":62,"length":2},{"value":64,"volume":0}]}
            """);

        set.Ok.ShouldBeTrue(set.Text);

        var notes = bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.notes").Steps.ShouldNotBeNull();

        notes.Count.ShouldBe(3);
        notes[0].ShouldBe(new Step(60f));
        notes[1].ShouldBe(new Step(62f, 2f));
        notes[2].ShouldBe(new Step(64f, 1f, 0f));
    }

    /// <summary>Replaced outright, so a shorter tune does not leave the tail of a longer one behind.</summary>
    [Fact]
    public async Task Setting_the_notes_replaces_the_whole_tune()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.notes","handle":"tune1"}""");

        await Call(bench, "set_steps", """{"handle":"tune1","notes":[{"value":1},{"value":2}]}""");
        await Call(bench, "set_steps", """{"handle":"tune1","notes":[{"value":9}]}""");

        bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.notes").Steps!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_module_with_no_notes_says_so_rather_than_growing_some()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");

        var set = await Call(bench, "set_steps", """{"handle":"tone1","notes":[{"value":1}]}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("no notes");
    }

    [Fact]
    public async Task More_notes_than_a_sequence_holds_is_refused()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.values","handle":"tune1"}""");

        var many = string.Join(",", Enumerable.Range(0, NodeCatalog.MaxSteps + 1).Select(_ => """{"value":0.5}"""));
        var set = await Call(bench, "set_steps", $$"""{"handle":"tune1","notes":[{{many}}]}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain(NodeCatalog.MaxSteps.ToString());
    }

    /// <summary>
    /// A tune is neither wiring nor a knob, so without this a sequencer reads as
    /// a module with nothing set on it and the model rewrites what was already
    /// right.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_shows_the_tune()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.notes","handle":"tune1"}""");
        await Call(bench, "set_steps", """{"handle":"tune1","notes":[{"value":60},{"value":67}]}""");

        (await Call(bench, "describe_patch")).Text.ShouldContain("60 67");
    }

    [Fact]
    public void The_briefing_says_a_sequencer_carries_a_list()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("set_steps");
        briefing.ShouldContain("notes");
    }

    /// <summary>
    /// A sink standing on its own compiles to a constant — a flat field, or
    /// silence — which is exactly what an assistant cannot tell apart from a
    /// patch that works.
    /// </summary>
    [Fact]
    public async Task A_sink_with_nothing_wired_into_it_is_said_out_loud()
    {
        var bench = Bench();

        var added = await Call(bench, "add_module", """{"type_id":"osc.sine"}""");

        added.Text.ShouldContain("Nothing is wired into the Output");
    }

    [Fact]
    public async Task Wiring_the_sink_up_settles_it()
    {
        var lit = await Lit();

        (await Call(lit, "describe_patch")).Text.ShouldNotContain("Nothing is wired into");
    }

    /// <summary>
    /// The mistake an assistant could not catch for itself — it cannot hear the
    /// patch, and a still picture looks like one that works — and it cannot be
    /// made any more: an oscillator with nothing in its 'in' is reading the
    /// clock it is normalled to, so there is nothing to say about it.
    /// </summary>
    [Fact]
    public async Task An_oscillator_with_nothing_driving_it_runs_on_what_it_is_normalled_to()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        var wired = await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"color"}""");

        wired.Text.ShouldContain("No issues.");
    }

    /// <summary>
    /// And the description says so, which is the half that matters here: an
    /// assistant reading "in = 0" would go on believing it had to wire a clock
    /// up, and reading nothing at all would not know what the socket was doing.
    /// </summary>
    [Fact]
    public async Task A_normalled_socket_is_described_as_wired_without_a_wire()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        var told = await Call(bench, "describe_patch");

        told.Text.ShouldContain("in <- Time (normalled, no wire)");
    }

    /// <summary>
    /// There is no knob behind a normalled socket, and an assistant that set one
    /// would watch the patch not change. Refused with the reason, rather than
    /// stored where nothing will read it.
    /// </summary>
    [Fact]
    public async Task Turning_a_knob_that_is_normalled_is_refused_with_the_reason()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");

        var turned = await Call(bench, "set_knobs", """
            {"handle":"tone1","knobs":[{"port":"in","value":0.25}]}
            """);

        turned.Ok.ShouldBeFalse();
        turned.Text.ShouldContain("normalled to Time");
        turned.Text.ShouldContain("Value module");
    }

    /// <summary>
    /// The bug this exists for. Compiling backwards from the screen means the
    /// video pass stops at the first line when there is no screen, so every
    /// edit on a patch built for the speakers came back "No issues." — however
    /// broken it was. An assistant cannot hear the patch, so that string was the
    /// only thing standing between it and shipping silence, and it was lying.
    /// </summary>
    /// <remarks>
    /// A cycle is the fault used here because it is one only the speakers reach:
    /// nothing is wired into 'color', so the video pass stops at the first line
    /// and never sees it. What is on trial is the second compilation happening
    /// at all, not what it happens to find.
    /// </remarks>
    [Fact]
    public async Task A_fault_only_the_speakers_reach_is_still_reported_on_every_edit()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"sum1"}""");
        await Call(bench, "add_module", """{"type_id":"math.add","handle":"sum2"}""");
        await Call(bench, "connect", """{"from":"sum1","to":"sum2","to_port":"a"}""");
        await Call(bench, "connect", """{"from":"sum2","to":"sum1","to_port":"a"}""");
        var wired = await Call(bench, "connect", """{"from":"sum2","to":"output1","to_port":"left"}""");

        wired.Text.ShouldContain("Issues:");
        wired.Text.ShouldContain("feeds back into itself");
    }

    /// <summary>
    /// A module both sinks reach is compiled twice, and being told about it
    /// twice in one breath reads as two separate problems.
    /// </summary>
    [Fact]
    public async Task A_module_both_sinks_reach_is_only_complained_about_once()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"sum1"}""");
        await Call(bench, "add_module", """{"type_id":"math.add","handle":"sum2"}""");
        await Call(bench, "connect", """{"from":"sum1","to":"sum2","to_port":"a"}""");
        await Call(bench, "connect", """{"from":"sum2","to":"sum1","to_port":"a"}""");
        await Call(bench, "connect", """{"from":"sum2","to":"output1","to_port":"color"}""");
        var wired = await Call(bench, "connect", """{"from":"sum2","to":"output1","to_port":"left"}""");

        var said = wired.Text;
        var first = said.IndexOf("feeds back into itself", StringComparison.Ordinal);

        first.ShouldBeGreaterThan(-1);
        said.IndexOf("feeds back into itself", first + 1, StringComparison.Ordinal).ShouldBe(-1);
    }

    [Fact]
    public async Task A_driven_oscillator_is_not_remarked_on()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"time","handle":"clock1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        await Call(bench, "connect", """{"from":"clock1","to":"tone1","to_port":"in"}""");
        var wired = await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"color"}""");

        wired.Text.ShouldContain("No issues.");
    }

    /// <summary>
    /// A patch that does not move is still a patch. The person may have meant a
    /// still, and a warning that blocked would be an error wearing a hat.
    /// </summary>
    /// <remarks>
    /// Held still by a Value on the oscillator's 'in', which is what standing
    /// one still now takes: the socket carries the clock unless something says
    /// otherwise, so a constant there is a decision somebody made rather than
    /// one they failed to.
    /// </remarks>
    [Fact]
    public async Task A_patch_that_does_not_move_can_still_be_proposed()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"value","handle":"knob1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        await Call(bench, "connect", """{"from":"knob1","to":"tone1","to_port":"in"}""");
        await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"color"}""");

        var offered = await Call(bench, "propose", """{"summary":"a flat field"}""");

        offered.Ok.ShouldBeTrue(offered.Text);
        bench.HasProposal.ShouldBeTrue();
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
        told.Text.ShouldContain("output1 = output");
        told.Text.ShouldContain("color <- knob1.out");
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
    /// A patch for the speakers draws nothing, and the compiler no longer says
    /// so — that is the point of it. Rendering one anyway would hand back a
    /// black rectangle, which is the one thing an assistant must never be shown
    /// for a patch that is working.
    /// </summary>
    [Fact]
    public async Task A_patch_with_no_screen_is_not_rendered_black_at_it()
    {
        var looked = await Call(await Heard(), "render");

        looked.Ok.ShouldBeFalse();
        looked.Png.ShouldBeNull();
        looked.Text.ShouldContain("'color'");
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
            {"type_id":"color.gain","handle":"gain1","knobs":[{"port":"gain","value":1},{"port":"bias","value":0.1}]}
            """);

        await Call(bench, "connect", """{"from":"previous1","to":"gain1","to_port":"color"}""");
        var wired = await Call(bench, "connect", """{"from":"gain1","to":"output1","to_port":"color"}""");
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

    // --- listening ----------------------------------------------------------

    [Fact]
    public void Listening_is_offered_only_when_the_model_can_hear()
    {
        Bench().Tools.Select(t => t.Name).ShouldContain("listen");
        Bench(hearing: false).Tools.Select(t => t.Name).ShouldNotContain("listen");
    }

    /// <summary>
    /// Off by default where sight is on, because a sound reaches only the few
    /// models built to take one and a picture reaches all of them.
    /// </summary>
    [Fact]
    public void Hearing_is_not_assumed_the_way_sight_is()
    {
        var tools = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch()).Tools.Select(t => t.Name).ToArray();

        tools.ShouldContain("render");
        tools.ShouldNotContain("listen");
    }

    [Fact]
    public async Task A_listen_is_a_wav_of_the_length_it_says()
    {
        var heard = await Call(await Heard(), "listen", """{"seconds":1}""");

        heard.Ok.ShouldBeTrue(heard.Text);
        heard.Wav.ShouldNotBeNull();
        heard.Png.ShouldBeNull();

        Riff(heard.Wav).ShouldBe("RIFF");
        Format(heard.Wav).ShouldBe("WAVE");
        Channels(heard.Wav).ShouldBe(2);
        Rate(heard.Wav).ShouldBe(24_000);

        // One second, stereo, two bytes a sample.
        DataBytes(heard.Wav).ShouldBe(24_000 * 2 * 2);
    }

    /// <summary>
    /// The caption is the only thing said about a payload the model hears rather
    /// than reads, so the number in it has to be the number in the file. A sine
    /// through the Output's default gain of 0.5 peaks at half of full scale,
    /// which is −6 dBFS.
    /// </summary>
    [Fact]
    public async Task What_it_is_told_about_the_level_is_the_level_it_is_sent()
    {
        var heard = await Call(await Heard(), "listen", """{"seconds":0.5}""");

        heard.Text.ShouldContain("Peak -6");
        heard.Text.ShouldContain("dBFS");
    }

    /// <summary>
    /// The counterpart of the black-frame rule. A patch built for the screen is
    /// silent on purpose and the compiler no longer remarks on it, so playing
    /// one would hand back two seconds of nothing — which reads as a broken tool
    /// rather than as a patch that was never meant to make a sound.
    /// </summary>
    [Fact]
    public async Task A_patch_with_no_sound_is_not_played_silence_at_it()
    {
        var heard = await Call(await Lit(0.5f), "listen");

        heard.Ok.ShouldBeFalse();
        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("'left'");
    }

    /// <summary>
    /// A patch that is wired, compiles without a word, and makes no sound. The
    /// compiler catches the loud version of this — an oscillator with nothing at
    /// all on its 'in' is a warning — so what is left for the ear is the quiet
    /// version: everything correct, and the gain at zero. Playing the model half
    /// a second of nothing would tell it far less than the sentence does, and
    /// costs a payload to say it.
    /// </summary>
    [Fact]
    public async Task A_patch_that_compiles_cleanly_and_makes_no_sound_comes_back_as_a_sentence()
    {
        var bench = await Heard();

        var turned = await Call(bench, "set_knobs", """
            {"handle":"output1","knobs":[{"port":"gain","value":0}]}
            """);
        turned.Ok.ShouldBeTrue(turned.Text);

        var heard = await Call(bench, "listen", """{"seconds":0.5}""");

        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("silence");
        heard.Text.ShouldContain("gain");
    }

    /// <summary>
    /// The other half of the same rule, and the one the compiler does see:
    /// anything it has to say is enough to stop this. Rendering it anyway would
    /// spend a payload on a recording of a patch already diagnosed in words.
    /// </summary>
    [Fact]
    public async Task A_fault_the_compiler_can_see_is_answered_by_it_and_not_by_the_ear()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"math.add","handle":"sum1"}""");
        await Call(bench, "add_module", """{"type_id":"math.add","handle":"sum2"}""");
        await Call(bench, "connect", """{"from":"sum1","to":"sum2","to_port":"a"}""");
        await Call(bench, "connect", """{"from":"sum2","to":"sum1","to_port":"a"}""");
        var wired = await Call(bench, "connect", """{"from":"sum2","to":"output1","to_port":"left"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var heard = await Call(bench, "listen", """{"seconds":0.5}""");

        heard.Ok.ShouldBeFalse();
        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("feeds back into itself");
    }

    /// <summary>
    /// An oscillator with nothing patched into it used to be the case above.
    /// It is a sound now, and one that can be listened to: the socket carries
    /// the clock without a wire, so there is nothing for the compiler to catch
    /// and nothing to keep the ear from being asked.
    /// </summary>
    [Fact]
    public async Task An_oscillator_with_nothing_driving_it_can_be_heard()
    {
        var bench = Bench();

        await Call(bench, "add_module", """
            {"type_id":"osc.sine","handle":"tone1","knobs":[{"port":"freq","value":440}]}
            """);
        await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"left"}""");

        var heard = await Call(bench, "listen", """{"seconds":0.5}""");

        heard.Ok.ShouldBeTrue(heard.Text);
        heard.Wav.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_patch_that_does_not_compile_cannot_be_listened_to()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        var heard = await Call(bench, "listen");

        heard.Ok.ShouldBeFalse();
        heard.Wav.ShouldBeNull();
    }

    /// <summary>
    /// What one call may spend is the workbench's to decide, not the model's:
    /// this goes into a request body as base64 and is paid for again on every
    /// turn that follows.
    /// </summary>
    [Fact]
    public async Task A_listen_longer_than_the_limit_is_cut_to_it()
    {
        var bench = new PatchWorkbench(
            NodeCatalog.BuiltIn,
            (await Heard()).Snapshot(),
            vision: false,
            hearing: true,
            new WorkbenchLimits(LongestListen: 1d));

        var heard = await Call(bench, "listen", """{"seconds":30}""");

        heard.Wav.ShouldNotBeNull();
        DataBytes(heard.Wav).ShouldBe(24_000 * 2 * 2);
    }

    /// <summary>
    /// The audio path is the one with a memory — delay lines and
    /// <c>feedback.unit</c>, per ADR-0027 — so a stretch starting at two seconds
    /// has to arrive with the two seconds behind it having actually happened.
    /// A phasing patch is the cheapest thing that proves it: a delayed copy
    /// against the original cancels differently once the line has filled.
    /// </summary>
    [Fact]
    public async Task Sound_from_further_in_is_warmed_up_to_rather_than_sought_to()
    {
        var bench = await Heard();

        var start = await Call(bench, "listen", """{"seconds":0.5}""");
        var later = await Call(bench, "listen", """{"seconds":0.5,"from":2}""");

        start.Wav.ShouldNotBeNull();
        later.Wav.ShouldNotBeNull();

        // Same length, different samples: a 440 Hz sine two seconds along is not
        // where it was at zero, and a renderer that had been sought rather than
        // run would hand back the same buffer twice.
        DataBytes(later.Wav).ShouldBe(DataBytes(start.Wav));
        later.Wav.ShouldNotBe(start.Wav);
    }

    // --- what comes out -----------------------------------------------------

    [Fact]
    public async Task A_snapshot_is_laid_out_left_to_right_with_the_screen_last()
    {
        var patch = (await Lit()).Snapshot();

        var knob = patch.Nodes.Single(n => n.TypeId == "value");
        var screen = patch.Nodes.Single(n => n.TypeId == NodeCatalog.OutputTypeId);

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
        // Two, because the Output is already one of them before anything is added.
        var bench = Bench(new WorkbenchLimits(MaxNodes: 2));

        (await Call(bench, "add_module", """{"type_id":"osc.sine"}""")).Ok.ShouldBeTrue();

        var second = await Call(bench, "add_module", """{"type_id":"osc.saw"}""");
        second.Ok.ShouldBeFalse();
        second.Text.ShouldContain("2 modules");
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

    // A RIFF header, read at the offsets WavWriter writes it at. Little-endian,
    // where PNG is big — which is most of why these are two sets of helpers.
    private static string Riff(byte[] wav) => System.Text.Encoding.ASCII.GetString(wav, 0, 4);

    private static string Format(byte[] wav) => System.Text.Encoding.ASCII.GetString(wav, 8, 4);

    private static int Channels(byte[] wav) => LittleEndian(wav, 22, 2);

    private static int Rate(byte[] wav) => LittleEndian(wav, 24, 4);

    private static int DataBytes(byte[] wav) => LittleEndian(wav, 40, 4);

    private static int LittleEndian(byte[] bytes, int at, int width)
    {
        var value = 0;
        for (var i = width - 1; i >= 0; i--) value = (value << 8) | bytes[at + i];
        return value;
    }
}
