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
    // --- proposing ----------------------------------------------------------

    [Fact]
    public async Task A_patch_that_does_not_compile_is_not_worth_proposing()
    {
        var bench = Bench();

        // A clip naming a file that is not there. The tools allow one to be set —
        // the compiler is what refuses it — so this is how a genuine fault gets
        // in.
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");
        await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}""");
        await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"color"}""");

        var offered = await Call(bench, "propose", """{"summary":"a tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("gone.wav");
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
    /// The audio branch can be the whole point of a patch, so it has to be
    /// compiled before one is offered: the video pass never reaches a node only
    /// the ear does, so on its own it would have said the patch was fine.
    /// </summary>
    [Fact]
    public async Task A_fault_only_the_speakers_reach_still_stops_a_proposal()
    {
        var bench = await Heard();

        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");
        await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}""");
        await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"right"}""");

        var offered = await Call(bench, "propose", """{"summary":"a 440 hz tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("gone.wav");
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
    /// A frequency wired straight into 'left' is a constant: pure DC, which the DC
    /// blocker removes. Wired, compiling and silent, so it is not offered as a sound.
    /// </summary>
    [Fact]
    public async Task A_patch_whose_sound_is_silent_is_not_proposed()
    {
        var bench = Bench(hearing: Listener.None);

        await Call(bench, "add_module", """{"type_id":"audio.note","handle":"pitch1"}""");
        await Call(bench, "connect", """{"from":"pitch1","from_port":"hz","to":"output1","to_port":"left"}""");

        var offered = await Call(bench, "propose", """{"summary":"slow glassy bells"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("silence");
        offered.Text.ShouldContain("DC");
        bench.HasProposal.ShouldBeFalse();
    }

    /// <summary>
    /// A patch may be meant to start silent, an intro that comes in later than the
    /// workbench listens. Saying so offers it anyway.
    /// </summary>
    [Fact]
    public async Task A_patch_said_to_start_silent_is_proposed_as_it_stands()
    {
        var bench = await Heard();

        await Call(bench, "set_knobs", """{"handle":"output1","knobs":[{"port":"volume","value":0}]}""");

        var offered = await Call(bench, "propose", """{"summary":"an intro","starts_silent":true}""");

        offered.Ok.ShouldBeTrue(offered.Text);
        bench.HasProposal.ShouldBeTrue();
    }

    /// <summary>
    /// An assistant reads this after every edit, so a patch built for the
    /// speakers alone must not trip the missing-screen warning — one that read
    /// that as something to fix would spend the run fixing it.
    /// </summary>
    [Fact]
    public async Task A_patch_built_for_the_speakers_is_not_nagged_about_the_screen()
    {
        var bench = await Heard();

        var told = await Call(bench, "describe_patch");

        told.Text.ShouldNotContain("no output");
        (await Call(bench, "set_knobs", """
            {"handle":"output1","knobs":[{"port":"volume","value":0.8}]}
            """)).Text.ShouldContain("No issues.");
    }

    /// <summary>
    /// The moment anything reaches the sink the complaint goes, which is what
    /// stops it being noise for the whole of the rest of the run. The block is
    /// always there, so the only thing left to say is that nothing arrives at it.
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
}
