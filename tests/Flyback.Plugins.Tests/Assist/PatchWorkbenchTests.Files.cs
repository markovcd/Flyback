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
    // --- a player's file ------------------------------------------------------

    /// <summary>
    /// The path is neither a knob nor a wire, so this is the only way to set
    /// one — and it is the one thing in a patch that refers outside it.
    /// </summary>
    [Fact]
    public async Task A_players_file_can_be_pointed_at()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");

        var set = await Call(bench, "set_sample", """{"handle":"clip1","path":"drums.wav"}""");

        set.Ok.ShouldBeTrue(set.Text);
        SampleExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == NodeCatalog.SampleTypeId))
            .ShouldBe("drums.wav");
    }

    /// <summary>
    /// Answered by the compiler rather than taken on trust, so a path that is
    /// wrong is known now. Nothing in these tests can read a file, so every path
    /// is wrong here — which is exactly the case worth checking.
    /// </summary>
    [Fact]
    public async Task A_file_that_cannot_be_read_is_said_so_by_the_call_that_set_it()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");
        await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"left"}""");

        var set = await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}""");

        set.Text.ShouldContain("gone.wav");
        set.Text.ShouldContain("Issues:");
    }

    /// <summary>
    /// A path is stored as the assistant gave it; the check is where every patch's files
    /// are read, so a share is never followed and a file that is not a sound or a picture
    /// never leaves in a bundle.
    /// </summary>
    [Theory]
    [InlineData("set_sample", NodeCatalog.SampleTypeId, @"\\host\share\x.wav")]
    [InlineData("set_sample", NodeCatalog.SampleTypeId, "../../x.wav")]
    [InlineData("set_sample", NodeCatalog.SampleTypeId, "/etc/passwd")]
    [InlineData("set_picture", NodeCatalog.PictureTypeId, @"\\host\share\x.png")]
    [InlineData("set_picture", NodeCatalog.PictureTypeId, "../../x.png")]
    [InlineData("set_picture", NodeCatalog.PictureTypeId, "/etc/passwd")]
    public async Task A_hostile_path_is_checked_where_every_patchs_files_are(string tool, string typeId, string path)
    {
        var root = Directory.CreateTempSubdirectory("flyback-hostile").FullName;

        try
        {
            var beside = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;
            File.WriteAllText(Path.Combine(root, Path.GetFileName(path)), "-----BEGIN OPENSSH PRIVATE KEY-----");

            var bench = Bench();
            await Call(bench, "add_module", JsonSerializer.Serialize(new { type_id = typeId, handle = "file1" }));

            var set = await Call(bench, tool, JsonSerializer.Serialize(new { handle = "file1", path }));
            set.Ok.ShouldBeTrue(set.Text);

            using var bundle = new MemoryStream();
            var report = PatchBundle.Write(bundle, bench.Snapshot(), named => PatchPaths.Carriable(named, beside), NodeCatalog.BuiltIn);

            report.Carried.ShouldBeEmpty();
            report.Missing.ShouldBe([path]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_module_that_reads_no_file_says_so_rather_than_growing_one()
    {
        var bench = Bench();
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");

        var set = await Call(bench, "set_sample", """{"handle":"tone1","path":"drums.wav"}""");

        set.Ok.ShouldBeFalse();
        set.Text.ShouldContain("reads no file");
    }

    /// <summary>
    /// The other kind of file, and its own tool for the reason a sample has one:
    /// a path is neither a knob nor a wire, and an Image with nothing pointed at
    /// it shows black. The extra tool is what stops a model placing one and
    /// having no way to finish it.
    /// </summary>
    [Fact]
    public async Task An_images_picture_can_be_pointed_at()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.PictureTypeId}}","handle":"shown1"}""");

        var set = await Call(bench, "set_picture", """{"handle":"shown1","path":"moon.png"}""");

        set.Ok.ShouldBeTrue(set.Text);
        PictureExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId))
            .ShouldBe("moon.png");

        // And refused on anything that shows none, rather than quietly stored.
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");

        var wrong = await Call(bench, "set_picture", """{"handle":"tone1","path":"moon.png"}""");

        wrong.Ok.ShouldBeFalse();
        wrong.Text.ShouldContain("shows no picture");
    }

    /// <summary>
    /// A path is neither wiring nor a knob, so without this a player reads as a
    /// module with nothing set on it and the model would set it again.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_shows_which_file_a_player_reads()
    {
        var bench = Bench();
        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");

        (await Call(bench, "describe_patch")).Text.ShouldNotContain("drums.wav");

        await Call(bench, "set_sample", """{"handle":"clip1","path":"drums.wav"}""");

        (await Call(bench, "describe_patch")).Text.ShouldContain("sample(\"drums.wav\")");
    }

    [Fact]
    public void The_briefing_says_a_player_carries_a_file()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("set_sample");
        briefing.ShouldContain("a path to a WAV or an MP3");
    }

    private static IReadOnlyList<int> Scale(PatchWorkbench bench) =>
        ScaleExtra.Of(bench.Snapshot().Nodes
            .Single(n => n.TypeId == NodeCatalog.QuantiserTypeId));

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

        told.Text.ShouldContain("carrying a signal with no wire");
        told.Text.ShouldContain("tone1.in <- Time");
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
    /// A model told only that a knob was refused goes on wiring the module it
    /// thinks it has, so the refusal says the module never arrived.
    /// </summary>
    [Fact]
    public async Task Adding_a_module_with_a_refused_knob_says_it_was_not_added()
    {
        var bench = Bench();

        var added = await Call(bench, "add_module", """
            {"type_id":"osc.sine","handle":"tone1","knobs":[{"port":"in","value":0.25}]}
            """);

        added.Ok.ShouldBeFalse();
        added.Text.ShouldContain("tone1 was not added");
        (await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"left"}""")).Ok.ShouldBeFalse();
    }

    /// <summary>
    /// The bug this exists for. Compiling backwards from the screen means the video
    /// pass stops at the first line when there is no screen, so every edit on a patch
    /// built for the speakers came back "No issues." — however broken it was, and an
    /// assistant cannot hear the patch.
    /// </summary>
    /// <remarks>
    /// A clip naming a file that is not there is the fault used here, because a
    /// Sample is a module only the speakers reach. What is on trial is the second
    /// compilation happening at all, not what it finds.
    /// </remarks>
    [Fact]
    public async Task A_fault_only_the_speakers_reach_is_still_reported_on_every_edit()
    {
        var bench = Bench();

        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");
        await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}""");
        var wired = await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"left"}""");

        wired.Text.ShouldContain("Issues:");
        wired.Text.ShouldContain("gone.wav");
    }

    /// <summary>
    /// A module both sinks reach is compiled twice, and being told about it
    /// twice reads as two separate problems. Once said, a complaint is counted on
    /// later edits rather than said again: the model has it already.
    /// </summary>
    [Fact]
    public async Task A_module_both_sinks_reach_is_only_complained_about_once()
    {
        var bench = Bench();

        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");

        var replies = new[]
        {
            await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}"""),
            await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"color"}"""),
            await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"left"}"""),
        };

        var said = string.Join("\n", replies.Select(reply => reply.Text));
        var first = said.IndexOf("cannot read gone.wav", StringComparison.Ordinal);

        first.ShouldBeGreaterThan(-1);
        said.IndexOf("cannot read gone.wav", first + 1, StringComparison.Ordinal).ShouldBe(-1, said);
        replies[^1].Text.ShouldContain("as said before");
    }

    [Fact]
    public async Task A_driven_oscillator_is_not_remarked_on()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"time","handle":"clock1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        await Call(bench, "connect", """{"from":"clock1","from_port":"t","to":"tone1","to_port":"in"}""");
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

        var offered = await Call(bench, "propose", """{"summary":"a flat gray field"}""");

        offered.Ok.ShouldBeTrue(offered.Text);
        bench.HasProposal.ShouldBeTrue();
        bench.ProposalSummary.ShouldBe("a flat gray field");
    }
}
