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

    /// <summary>The path is the call's string, and the one thing in a patch that refers outside it.</summary>
    [Fact]
    public async Task A_players_file_can_be_pointed_at()
    {
        var bench = Bench();

        var set = await Write(bench, "let clip1 = sample(\"drums.wav\")");

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
        var set = await Write(Bench(), "let clip1 = sample(\"gone.wav\")\nclip1.out |> out.left");

        set.Text.ShouldContain("gone.wav");
        set.Text.ShouldContain("Issues:");
    }

    /// <summary>
    /// A path is stored as the assistant gave it; the check is where every patch's files
    /// are read, so a share is never followed and a file that is not a sound or a picture
    /// never leaves in a bundle.
    /// </summary>
    [Theory]
    [InlineData("sample", @"\\host\share\x.wav")]
    [InlineData("sample", "../../x.wav")]
    [InlineData("sample", "/etc/passwd")]
    [InlineData("picture", @"\\host\share\x.png")]
    [InlineData("picture", "../../x.png")]
    [InlineData("picture", "/etc/passwd")]
    public async Task A_hostile_path_is_checked_where_every_patchs_files_are(string call, string path)
    {
        var root = Directory.CreateTempSubdirectory("flyback-hostile").FullName;

        try
        {
            var beside = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;
            File.WriteAllText(Path.Combine(root, Path.GetFileName(path)), "-----BEGIN OPENSSH PRIVATE KEY-----");

            var bench = Bench();

            var set = await Write(bench, $"let file1 = {call}(\"{path}\")");
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

    /// <summary>
    /// The other kind of file, and the same spelling: an Image's picture is its call's
    /// string, and a module that shows none has no string to take.
    /// </summary>
    [Fact]
    public async Task An_images_picture_can_be_pointed_at()
    {
        var bench = Bench();

        var set = await Write(bench, "let shown1 = picture(\"moon.png\")");

        set.Ok.ShouldBeTrue(set.Text);
        PictureExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId))
            .ShouldBe("moon.png");

        var wrong = await Write(bench, "let tone1 = sine(\"moon.png\")");

        wrong.Ok.ShouldBeFalse();
        PictureExtra.Of(bench.Snapshot().Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId))
            .ShouldBe("moon.png");
    }

    /// <summary>
    /// A path is neither wiring nor a knob, so without this a player reads as a
    /// module with nothing set on it and the model would set it again.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_shows_which_file_a_player_reads()
    {
        static PatchWorkbench Drawn(string source) =>
            new(NodeCatalog.BuiltIn, PatchLanguage.Build(source, NodeCatalog.BuiltIn).Patch);

        (await Call(Drawn("let clip1 = sample()"), "describe_patch")).Text.ShouldNotContain("drums.wav");
        (await Call(Drawn("let clip1 = sample(\"drums.wav\")"), "describe_patch")).Text.ShouldContain("sample(\"drums.wav\")");
    }

    [Fact]
    public void The_briefing_says_a_player_carries_a_file()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("a path to a WAV or an MP3 — not a knob, written as the call's one unnamed string");
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
        var added = await Write(Bench(), "let tone = sine()");

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
        var wired = await Write(Bench(), "let tone1 = sine()\ntone1 |> out.color");

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

        await Write(bench, "let tone1 = sine()");
        var told = await Call(bench, "describe_patch");

        told.Text.ShouldContain("carrying a signal with no wire");
        told.Text.ShouldContain("tone1.in <- Time");
    }

    /// <summary>
    /// There is no knob behind a normalled socket, and an assistant that set one
    /// would watch the patch not change, so a number written on one is refused.
    /// </summary>
    [Fact]
    public async Task Turning_a_knob_that_is_normalled_is_refused_with_the_reason()
    {
        var bench = Bench();

        var turned = await Write(bench, "let tone1 = sine(in: 0.25)\ntone1 |> out.color");

        turned.Ok.ShouldBeFalse();
        turned.Text.ShouldContain("normalled");
        turned.Text.ShouldContain("Value");
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
        var wired = await Write(Bench(), "let clip1 = sample(\"gone.wav\")\nclip1.out |> out.left");

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

        var replies = new[]
        {
            await Write(bench, "let clip1 = sample(\"gone.wav\")\nclip1.out |> out.color"),
            await Edit(bench, "clip1.out |> out.color", "clip1.out |> out.color\nclip1.out |> out.left"),
            await Edit(bench, "clip1.out |> out.left", "clip1.out |> out.left\nout.volume = 0.8"),
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
        var wired = await Write(Bench(), "let tone1 = t |> sine()\ntone1 |> out.color");

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

        (await Write(bench, "let knob1 = value()\nlet tone1 = sine(in: knob1)\ntone1 |> out.color")).Ok.ShouldBeTrue();

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
