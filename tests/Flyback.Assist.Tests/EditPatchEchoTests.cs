using System.Text.Json;
using Flyback.Assist;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Assist.Tests;

/// <summary>What <c>edit_patch</c> answers with: the lines it changed, as they now read.</summary>
public class EditPatchEchoTests
{
    private const string Source = """
        let a = sine(freq: 1)
        let b = sine(freq: 2)
        let c = sine(freq: 3)
        let d = sine(freq: 4)
        a |> out.left
        """;

    private static async Task<PatchWorkbench> Written(string source)
    {
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);

        (await Call(bench, "write_patch", JsonSerializer.Serialize(new { source }))).Ok.ShouldBeTrue();

        return bench;
    }

    private static Task<ToolOutcome> Call(PatchWorkbench bench, string tool, string arguments) =>
        bench.InvokeAsync(tool, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None);

    private static Task<ToolOutcome> Edit(PatchWorkbench bench, params (string Find, string Replace)[] edits) =>
        Call(bench, "edit_patch", JsonSerializer.Serialize(new { edits = edits.Select(e => new { find = e.Find, replace = e.Replace }) }));

    /// <summary>The numbered lines of an answer.</summary>
    private static string[] Echoed(ToolOutcome outcome) =>
        [.. outcome.Text.ReplaceLineEndings("\n").Split('\n').Where(line => line.Length > 5 && line[4] == '|')];

    [Fact]
    public async Task Lines_are_echoed_in_the_order_they_stand()
    {
        var bench = await Written(Source);

        var edited = await Edit(bench, ("freq: 4", "freq: 40"), ("freq: 2", "freq: 20"));

        edited.Ok.ShouldBeTrue(edited.Text);
        Echoed(edited).ShouldBe(["   2| let b = sine(freq: 20)", "   4| let d = sine(freq: 40)"]);
    }

    [Fact]
    public async Task A_line_two_edits_touch_is_echoed_once_as_it_ends()
    {
        var bench = await Written(Source);

        var edited = await Edit(bench, ("freq: 3", "freq: 30"), ("freq: 30", "freq: 31"));

        Echoed(edited).ShouldBe(["   3| let c = sine(freq: 31)"]);
    }

    [Fact]
    public async Task A_line_an_earlier_edit_moved_down_is_numbered_where_it_ends()
    {
        var bench = await Written(Source);

        var edited = await Edit(bench, ("freq: 4", "freq: 44"), ("let a = sine(freq: 1)", "let a = sine(freq: 1)\nlet e = sine(freq: 5)"));

        Echoed(edited).ShouldBe(["   1| let a = sine(freq: 1)", "   2| let e = sine(freq: 5)", "   5| let d = sine(freq: 44)"]);
    }

    [Fact]
    public async Task What_answers_to_a_handle_is_said_on_a_line_of_its_own()
    {
        var bench = await Written(Source);

        var edited = await Edit(bench, ("a |> out.left", "a |> out.left\nsine(freq: 5) |> out.right"));

        edited.Text.ShouldContain("\nThose with no let name answer to: ".ReplaceLineEndings());
    }
}
