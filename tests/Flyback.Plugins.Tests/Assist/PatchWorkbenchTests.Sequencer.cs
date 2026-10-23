using System.Globalization;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Language;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- a sequencer's notes ------------------------------------------------

    /// <summary>
    /// The notes are a list on the module rather than knobs (ADR-0038), written as
    /// a block after the call.
    /// </summary>
    [Fact]
    public async Task A_tune_can_be_written_in_one_call()
    {
        var bench = Bench();

        var set = await Write(bench, "let tune1 = notes() [ C4 D4@2 E4%0 ]");

        set.Ok.ShouldBeTrue(set.Text);

        var notes = StepsExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.notes"));

        notes.Count.ShouldBe(3);
        notes[0].ShouldBe(new Step(60f));
        notes[1].ShouldBe(new Step(62f, 2f));
        notes[2].ShouldBe(new Step(64f, 1f, 0f));
    }

    [Fact]
    public async Task An_arrangement_is_written_a_part_a_row()
    {
        var bench = Bench();

        var set = await Write(bench, "let song1 = arrangement() [ 1 0 >1 | 0.5 ]");

        set.Ok.ShouldBeTrue(set.Text);

        var parts = ArrangementExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.arrangement"));

        parts[0].ShouldBe([new PartLevel(1f), new PartLevel(0f), new PartLevel(1f, Glides: true)]);
        parts[1].Select(l => l.Value).ShouldBe([0.5f]);
    }

    /// <summary>Replaced outright, so a shorter tune does not leave the tail of a longer one behind.</summary>
    [Fact]
    public async Task Setting_the_notes_replaces_the_whole_tune()
    {
        var bench = Bench();

        await Write(bench, "let tune1 = notes() [ C4 D4 ]");
        (await Edit(bench, "[ C4 D4 ]", "[ A4 ]")).Ok.ShouldBeTrue();

        StepsExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == "seq.notes")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task More_notes_than_a_sequence_holds_is_refused()
    {
        var bench = Bench();

        var many = string.Join(" ", Enumerable.Range(0, NodeCatalog.MaxSteps + 1).Select(_ => "0.5"));
        var set = await Write(bench, $"let tune1 = values() [ {many} ]");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain(NodeCatalog.MaxSteps.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A tune is neither wiring nor a knob, so without this a sequencer reads as
    /// a module with nothing set on it and the model rewrites what was already
    /// right.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_shows_the_tune()
    {
        var drawn = PatchLanguage.Build("let tune1 = notes() [ C4 G4 ]", NodeCatalog.BuiltIn).Patch;

        (await Call(new PatchWorkbench(NodeCatalog.BuiltIn, drawn), "describe_patch")).Text.ShouldContain("[ C4 G4 ]");
    }

    [Fact]
    public void The_briefing_says_a_sequencer_carries_a_list()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("notes  a list of up to");
        briefing.ShouldContain("written as a block after the call, in step notation");
    }
}
