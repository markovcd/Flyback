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
    // --- writing a patch whole ------------------------------------------------

    /// <summary>
    /// Where <c>write_patch</c> sits in the list, which is not cosmetic. A model
    /// reads these in order and anchors on the first thing that looks like it
    /// edits — and with the placing tool second it did exactly that, building
    /// patches a wire at a time and running out of turns.
    /// </summary>
    [Fact]
    public void Writing_a_patch_is_offered_before_placing_one()
    {
        var names = Bench().Tools.Select(t => t.Name).ToList();

        names.IndexOf("write_patch").ShouldBeLessThan(names.IndexOf("add_module"));
        names[0].ShouldBe("describe_patch");
        names[1].ShouldBe("write_patch");
    }

    /// <summary>
    /// And the briefing says the same thing. The instruction that decides how a
    /// model works is the one in "How to work", so naming the one-at-a-time
    /// tools there as the way to build was worth more than everything else the
    /// briefing says about the language.
    /// </summary>
    [Fact]
    public void The_briefing_says_to_build_by_writing_the_whole_patch()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("write_patch");
        briefing.ShouldContain("in one call");

        // The old instruction, which said to assemble a patch out of these.
        briefing.ShouldNotContain("Build with\n        `add_module`");
    }

    /// <summary>The placing tool points at the one that builds, for a model that reads it first.</summary>
    [Fact]
    public void Placing_one_module_points_at_writing_the_patch() =>
        Bench().Tools.Single(t => t.Name == "add_module").Description.ShouldContain("write_patch");

    /// <summary>
    /// The arithmetic this exists for. Placing a module is one call and so is
    /// every wire, which puts the largest preset in the box past
    /// <see cref="WorkbenchLimits.MaxToolCalls"/> before it is finished. Here it
    /// is one call.
    /// </summary>
    [Fact]
    public async Task A_whole_patch_can_be_written_in_one_call()
    {
        var bench = Bench();

        var written = await Call(bench, "write_patch", JsonSerializer.Serialize(new
        {
            source = """
                let slowly = t * 0.2
                let wave   = y |> sine(freq: 1.1, phase: slowly)

                x |> sine(freq: 1.5)
                  |> add(a: _, b: wave)
                  |> remap(-2..2, 0..1)
                  |> hsv(hue: _, saturation: 0.85, value: 1)
                  |> out.color
                """,
        }));

        written.Ok.ShouldBeTrue(written.Text);

        var patch = bench.Snapshot();

        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);
        patch.Reaches().Picture.ShouldBeTrue();
    }

    /// <summary>
    /// Nothing partial is adopted, and the complaint carries a line and a column
    /// so the next attempt can be aimed rather than guessed.
    /// </summary>
    [Fact]
    public async Task A_patch_that_does_not_read_is_refused_whole()
    {
        var bench = await Lit(0.25f);

        var written = await Call(bench, "write_patch", JsonSerializer.Serialize(new
        {
            source = "nonesuch() |> out.color",
        }));

        written.Ok.ShouldBeFalse();
        written.Text.ShouldContain("nonesuch");

        // The patch on the bench is the one that was there before.
        bench.Snapshot().Nodes.ShouldContain(n => n.TypeId == "value");
    }

    /// <summary>A group round one module costs the box, not the write, and the reply says so.</summary>
    [Fact]
    public async Task A_group_of_one_module_is_written_with_a_warning()
    {
        var bench = await Lit(0.25f);

        var written = await Call(bench, "write_patch", JsonSerializer.Serialize(new
        {
            source = "group \"Voice\" {\n  let tone = sine(freq: 220)\n}\ntone |> out.left",
        }));

        written.Ok.ShouldBeTrue(written.Text);
        written.Text.ShouldContain("[group-too-small] warning:");

        var patch = bench.Snapshot();

        (patch.Groups?.Count ?? 0).ShouldBe(0);
        patch.Nodes.ShouldContain(n => n.TypeId == "osc.sine");
    }

    /// <summary>A refusal names each mistake by its code, which a program can match on.</summary>
    [Fact]
    public async Task A_refusal_gives_each_mistake_its_code()
    {
        var bench = await Lit(0.25f);

        var written = await Call(bench, "write_patch", JsonSerializer.Serialize(new
        {
            source = "rotate() |> kaleidoscop(segments: 6) |> clouds() |> color.hsv(hue: _) |> out.color\n"
                + "pulse(freq: 2) |> adsr(decay: 240ms) |> out.left",
        }));

        written.Ok.ShouldBeFalse();
        written.Text.ShouldContain("[unknown-module]");
        written.Text.ShouldNotContain("kaleidoscope'");
        written.Text.ShouldContain("[pipe-lands-nowhere]");
    }

    /// <summary>
    /// How large a patch is is not the workbench's to refuse: Whole band has more
    /// modules than anything a person would write by hand.
    /// </summary>
    [Fact]
    public async Task A_large_patch_can_be_written_and_added_to()
    {
        var source = string.Join('\n', Enumerable.Range(0, 300).Select(i => $"let v{i} = value()"))
            + "\nrings() |> out.color";

        var bench = Bench();

        var written = await Call(bench, "write_patch", JsonSerializer.Serialize(new { source }));
        written.Ok.ShouldBeTrue(written.Text);
        bench.Snapshot().Nodes.Count.ShouldBeGreaterThan(300);

        (await Call(bench, "add_module", """{"type_id":"osc.sine"}""")).Ok.ShouldBeTrue();
    }

    /// <summary>
    /// What comes back from describing a patch goes back in as it stands. The
    /// two halves of the loop are the same language, which is the whole point of
    /// having only one.
    /// </summary>
    [Fact]
    public async Task What_is_described_can_be_written_back()
    {
        var first = await Heard();
        var described = (await Call(first, "describe_patch")).Text;

        // Everything before the counts, which are prose about the patch rather
        // than part of it.
        var source = described[..described.IndexOf(" modules, ", StringComparison.Ordinal)];
        source = source[..source.LastIndexOf('\n')];

        var second = Bench();
        var written = await Call(second, "write_patch", JsonSerializer.Serialize(new { source }));

        written.Ok.ShouldBeTrue(written.Text + Environment.NewLine + source);
        second.Snapshot().Reaches().Sound.ShouldBeTrue();
    }

    /// <summary>
    /// The Output answers to what the language calls it. describe_patch writes
    /// <c>out.left</c>, so that is the name that comes back to these tools —
    /// and refusing it cost a model three turns discovering that the block it
    /// had just read as 'out' was handled 'output1'.
    /// </summary>
    [Fact]
    public async Task The_output_answers_to_out_as_well_as_to_its_handle()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");

        var wired = await Call(bench, "connect", """{"from":"tone1","to":"out","to_port":"left"}""");

        wired.Ok.ShouldBeTrue(wired.Text);
        bench.Snapshot().Reaches().Sound.ShouldBeTrue();
    }

    [Fact]
    public async Task A_knob_on_the_output_can_be_turned_through_out()
    {
        var bench = Bench();

        var turned = await Call(bench, "set_knobs", """
            {"handle":"out","knobs":[{"port":"volume","value":0.8}]}
            """);

        turned.Ok.ShouldBeTrue(turned.Text);
        bench.Snapshot().Output.InputValues[NodeCatalog.OutputVolumePort].ShouldBe(0.8f);
    }

    /// <summary>A name nobody has is still refused, and still says what the patch does have.</summary>
    [Fact]
    public async Task A_handle_that_is_not_there_is_still_refused()
    {
        var refused = await Call(Bench(), "set_knobs", """
            {"handle":"nonesuch","knobs":[{"port":"volume","value":0.8}]}
            """);

        refused.Ok.ShouldBeFalse();
        refused.Text.ShouldContain("nonesuch");
    }
}
