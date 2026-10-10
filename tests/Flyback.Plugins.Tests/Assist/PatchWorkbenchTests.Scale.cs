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
    // --- a quantiser's scale --------------------------------------------------

    /// <summary>
    /// The other list a node carries, and the same rule: not knobs, so
    /// set_knobs cannot reach it and this is the only way to choose a scale.
    /// </summary>
    [Fact]
    public async Task A_scale_can_be_written_in_one_call()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.QuantiserTypeId}}","handle":"key1"}""");

        var set = await Call(bench, "set_scale", """{"handle":"key1","notes":[0,3,5,7,10]}""");

        set.Ok.ShouldBeTrue(set.Text);
        Scale(bench).ShouldBe([0, 3, 5, 7, 10]);
    }

    /// <summary>
    /// A set, so a model that sends one out of order or names a note twice gets
    /// the scale it meant rather than a refusal about something that does not
    /// matter.
    /// </summary>
    [Fact]
    public async Task A_scale_sent_out_of_order_or_twice_over_is_tidied()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.QuantiserTypeId}}","handle":"key1"}""");

        var set = await Call(bench, "set_scale", """{"handle":"key1","notes":[7,0,4,0,7]}""");

        set.Ok.ShouldBeTrue(set.Text);
        Scale(bench).ShouldBe([0, 4, 7]);
    }

    /// <summary>Replaced outright, so a smaller scale does not leave the rest of a larger one on.</summary>
    [Fact]
    public async Task Setting_the_scale_replaces_the_whole_of_it()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.QuantiserTypeId}}","handle":"key1"}""");

        await Call(bench, "set_scale", """{"handle":"key1","notes":[0,2,4,5,7,9,11]}""");
        await Call(bench, "set_scale", """{"handle":"key1","notes":[0,7]}""");

        Scale(bench).ShouldBe([0, 7]);
    }

    /// <summary>
    /// An empty scale is a state worth being able to ask for — the module
    /// passes its signal through — so it is set rather than refused.
    /// </summary>
    [Fact]
    public async Task An_empty_scale_is_allowed()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.QuantiserTypeId}}","handle":"key1"}""");

        var set = await Call(bench, "set_scale", """{"handle":"key1","notes":[]}""");

        set.Ok.ShouldBeTrue(set.Text);
        Scale(bench).ShouldBeEmpty();
        set.Text.ShouldContain("passes the signal through");
    }

    /// <summary>
    /// A note number is not a pitch class, and 60 for middle C is exactly the
    /// mistake a model steeped in MIDI will make — so the refusal says which of
    /// the two this wants.
    /// </summary>
    [Fact]
    public async Task A_note_number_in_place_of_a_pitch_class_is_refused_by_name()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.QuantiserTypeId}}","handle":"key1"}""");

        var set = await Call(bench, "set_scale", """{"handle":"key1","notes":[60,64,67]}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("pitch class");

        // And nothing was half applied: the scale it arrived with is intact.
        Scale(bench).ShouldBe([0, 2, 4, 5, 7, 9, 11]);
    }

    [Fact]
    public async Task A_module_with_no_scale_says_so_rather_than_growing_one()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");

        var set = await Call(bench, "set_scale", """{"handle":"tone1","notes":[0,4,7]}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("no scale");
    }

    /// <summary>
    /// A scale is neither wiring nor a knob, so without this a Quantiser reads
    /// as a module with nothing set on it — and the names are there because a
    /// row of numbers is not a scale anybody recognises.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_shows_the_scale_by_name()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.QuantiserTypeId}}","handle":"key1"}""");
        await Call(bench, "set_scale", """{"handle":"key1","notes":[0,4,7]}""");

        var told = (await Call(bench, "describe_patch")).Text;

        told.ShouldContain("[ C E G ]");
    }

    [Fact]
    public void The_briefing_says_a_quantiser_carries_a_scale()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("set_scale");
        briefing.ShouldContain("pitch class");
    }
}
