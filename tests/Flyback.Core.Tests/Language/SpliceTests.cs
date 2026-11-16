using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary><c>$name</c> in a block (ADR-0235): a name's number, note or list written into the block as the text is built.</summary>
public class SpliceTests
{
    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static Patch Built(string source)
    {
        var load = Try(source);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static float[] Steps(Patch patch) =>
        [.. patch.Nodes.Where(n => n.TypeId == "seq.notes").SelectMany(n => StepsExtra.Of(n).Select(s => s.Value))];

    [Fact]
    public void A_name_in_a_block_is_the_same_patch_as_the_note_written_there() =>
        Steps(Built("let root = A2\nnotes() [ $root C3 ] |> note(note: _) |> sine(freq: _) |> out.left"))
            .ShouldBe(Steps(Built("notes() [ A2 C3 ] |> note(note: _) |> sine(freq: _) |> out.left")));

    [Fact]
    public void A_list_in_a_block_is_its_items_one_step_each() =>
        Steps(Built("let riff = [A2, C3, E3]\nnotes() [ $riff ~ ] |> note(note: _) |> sine(freq: _) |> out.left"))
            .ShouldBe(Steps(Built("notes() [ A2 C3 E3 ~ ] |> note(note: _) |> sine(freq: _) |> out.left")));

    [Fact]
    public void A_loop_variable_varies_the_pattern_on_each_pass()
    {
        var patch = Built("""
            let lines = for root in [A2, F2] {
              notes() [ $root C3 ]
            }
            sum(lines) |> note(note: _) |> sine(freq: _) |> out.left
            """);

        Steps(patch).ShouldBe([45f, 48f, 41f, 48f]);
    }

    [Fact]
    public void A_name_takes_the_notation_after_it() =>
        Steps(Built("let root = A2\nnotes() [ $root!3 ] |> note(note: _) |> sine(freq: _) |> out.left"))
            .ShouldBe([45f, 45f, 45f]);

    [Fact]
    public void A_block_that_reads_a_name_is_not_rewritten_from_the_canvas()
    {
        var load = Try("let root = A2\nnotes() [ $root C3 ] |> note(note: _) |> sine(freq: _) |> out.left");
        var notes = load.Patch.Nodes.Single(n => n.TypeId == "seq.notes");

        load.Map.Carried(notes.Id, "[ A2 D3 ]").ShouldBeNull();
    }

    [Theory]
    [InlineData("notes() [ $nope ] |> out.left", IssueCode.UnknownName)]
    [InlineData("let s = sine()\nnotes() [ $s ] |> out.left", IssueCode.BadSplice)]
    [InlineData("let f = \"a.wav\"\nnotes() [ $f ] |> out.left", IssueCode.BadSplice)]
    [InlineData("let d = 20ms\nnotes() [ $d ] |> out.left", IssueCode.BadSplice)]
    [InlineData("notes() [ $ A2 ] |> out.left", IssueCode.BadSplice)]
    public void A_name_a_block_cannot_hold_is_known_by_its_code(string source, string code) =>
        Try(source).Issues.ShouldContain(issue => issue.Code == code, Try(source).Report);

    [Fact]
    public void A_complaint_about_a_name_points_at_it() =>
        Try("notes() [ A2\n  $nope ] |> out.left").Issues.ShouldContain(issue => issue.Line == 2 && issue.Column == 3);
}
