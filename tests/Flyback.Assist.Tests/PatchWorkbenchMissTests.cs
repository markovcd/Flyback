using System.Text.Json;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Assist.Tests;

/// <summary>A failed edit holds <c>propose</c> back, so a summary never describes a patch that is not there.</summary>
public class PatchWorkbenchMissTests
{
    private const string Tone = """
        let tone1 = t |> sine(freq: 440)
        tone1 |> out.left
        """;

    private static PatchWorkbench Bench(Delivery delivery = Delivery.Turn) =>
        new(NodeCatalog.BuiltIn, new Patch(), vision: false, delivery: delivery);

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments = "{}") =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    private static Task<ToolOutcome> Edit(PatchWorkbench bench, string find, string replace) =>
        Call(bench, "edit_patch", JsonSerializer.Serialize(new { edits = new[] { new { find, replace } } }));

    private static async Task<PatchWorkbench> Toned()
    {
        var bench = Bench();

        (await Call(bench, "write_patch", JsonSerializer.Serialize(new { source = Tone }))).Ok.ShouldBeTrue();

        return bench;
    }

    [Fact]
    public async Task A_proposal_after_an_edit_that_missed_is_refused()
    {
        var bench = await Toned();

        (await Edit(bench, "freq: 220", "freq: 330")).Ok.ShouldBeFalse();

        var offered = await Call(bench, "propose", """{"summary":"a 330 hz tone"}""");

        offered.Ok.ShouldBeFalse();
        offered.Text.ShouldContain("last edit did not go through");
        offered.Text.ShouldContain("not in the patch's text", Case.Insensitive);
        bench.HasProposal.ShouldBeFalse();
    }

    [Fact]
    public async Task A_write_that_does_not_read_holds_a_proposal_back_as_well()
    {
        var bench = await Toned();

        (await Call(bench, "write_patch", """{"source":"let x = nonesuch()"}""")).Ok.ShouldBeFalse();

        (await Call(bench, "propose", """{"summary":"a nonesuch"}""")).Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task An_edit_that_lands_after_a_miss_lets_the_proposal_through()
    {
        var bench = await Toned();

        (await Edit(bench, "freq: 220", "freq: 330")).Ok.ShouldBeFalse();
        (await Edit(bench, "freq: 440", "freq: 330")).Ok.ShouldBeTrue();

        var offered = await Call(bench, "propose", """{"summary":"a 330 hz tone"}""");

        offered.Ok.ShouldBeTrue(offered.Text);
    }

    [Fact]
    public async Task Looking_at_the_patch_after_a_miss_lets_what_stands_be_proposed()
    {
        var bench = await Toned();

        (await Edit(bench, "freq: 220", "freq: 330")).Ok.ShouldBeFalse();
        (await Call(bench, "describe_patch")).Ok.ShouldBeTrue();

        (await Call(bench, "propose", """{"summary":"a 440 hz tone"}""")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task A_miss_is_carried_by_a_workbench_put_away_and_restored()
    {
        var bench = await Toned();

        (await Edit(bench, "freq: 220", "freq: 330")).Ok.ShouldBeFalse();

        var state = bench.Save();
        var restored = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);
        restored.Restore(state);

        (await Call(restored, "propose", """{"summary":"a 330 hz tone"}""")).Ok.ShouldBeFalse();
    }

    [Fact]
    public void Each_delivery_is_told_what_propose_does_there()
    {
        var live = Bench(Delivery.Live);

        live.Tools.Single(tool => tool.Name == "propose").Description.ShouldContain("applies nothing");
        live.Tools.Single(tool => tool.Name == "propose").Description.ShouldNotContain("Nothing you have built reaches");
        live.Briefing.ShouldContain("Every edit is written to the patch file the moment it is made");
        live.Briefing.ShouldNotContain("Nothing reaches the person's editor until");

        var driven = Bench(Delivery.Proposal);

        driven.Tools.Single(tool => tool.Name == "propose").Description.ShouldContain("Puts the patch on the person's canvas");
        driven.Tools.Single(tool => tool.Name == "propose").Description.ShouldNotContain("ends your turn");
        driven.Briefing.ShouldContain("Nothing reaches the person's editor until");
        driven.Briefing.ShouldContain("Call `describe_patch` to see the patch");

        var conversation = Bench();

        conversation.Tools.Single(tool => tool.Name == "propose").Description.ShouldContain("This ends your turn");
        conversation.Briefing.ShouldContain("Nothing reaches the person's editor until");
        conversation.Briefing.ShouldContain("The first message opens with the patch");
    }
}
