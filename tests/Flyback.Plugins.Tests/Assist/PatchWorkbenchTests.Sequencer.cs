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

        var notes = StepsExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.notes"));

        notes.Count.ShouldBe(3);
        notes[0].ShouldBe(new Step(60f));
        notes[1].ShouldBe(new Step(62f, 2f));
        notes[2].ShouldBe(new Step(64f, 1f, 0f));
    }

    [Fact]
    public async Task An_arrangement_is_written_a_part_a_string()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.arrangement","handle":"song1"}""");

        var set = await Call(bench, "set_arrangement", """{"handle":"song1","parts":["1 0 >1","0.5"]}""");

        set.Ok.ShouldBeTrue(set.Text);

        var parts = ArrangementExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.arrangement"));

        parts[0].ShouldBe([new PartLevel(1f), new PartLevel(0f), new PartLevel(1f, Glides: true)]);
        parts[1].Select(l => l.Value).ShouldBe([0.5f, 0f, 0f]);
    }

    [Fact]
    public async Task A_part_that_does_not_read_is_refused_by_number()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.arrangement","handle":"song1"}""");

        var set = await Call(bench, "set_arrangement", """{"handle":"song1","parts":["1 1","1 loud"]}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("part 2");
    }

    /// <summary>Replaced outright, so a shorter tune does not leave the tail of a longer one behind.</summary>
    [Fact]
    public async Task Setting_the_notes_replaces_the_whole_tune()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"seq.notes","handle":"tune1"}""");

        await Call(bench, "set_steps", """{"handle":"tune1","notes":[{"value":1},{"value":2}]}""");
        await Call(bench, "set_steps", """{"handle":"tune1","notes":[{"value":9}]}""");

        StepsExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.notes")).Count.ShouldBe(1);
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

        (await Call(bench, "describe_patch")).Text.ShouldContain("[ C4 G4 ]");
    }

    [Fact]
    public void The_briefing_says_a_sequencer_carries_a_list()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("set_steps");
        briefing.ShouldContain("notes");
    }
}
