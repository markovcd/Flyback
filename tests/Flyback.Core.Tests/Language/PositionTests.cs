using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary><c>xs[i]</c> and <c>len(xs)</c> (ADR-0235): a list read by position as the text is built, counted from one.</summary>
public class PositionTests
{
    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static Patch Built(string source)
    {
        var load = Try(source);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static float[] Frequencies(Patch patch) =>
        [.. patch.Nodes.Where(n => n.TypeId == "osc.sine").Select(n => n.InputValues[1])];

    [Fact]
    public void An_item_is_read_by_its_position_counted_from_one() =>
        Frequencies(Built("""
            let fs = [110, 220, 330]
            sine(freq: fs[1]) + sine(freq: fs[3]) |> out.left
            """)).ShouldBe([110f, 330f], ignoreOrder: true);

    [Fact]
    public void A_loop_counts_a_list_with_len_and_reads_each_item() =>
        Frequencies(Built("""
            let chords = [(110, 220), (330, 440)]
            let voices = for i in 1..len(chords) {
              let (low, high) = chords[i]
              sine(freq: low) + sine(freq: high)
            }
            sum(voices) |> out.left
            """)).ShouldBe([110f, 220f, 330f, 440f], ignoreOrder: true);

    [Fact]
    public void A_position_can_be_reckoned_and_chained() =>
        Frequencies(Built("""
            let rows = [[100, 200], [300, 400]]
            sine(freq: rows[1 + 1][2 - 1]) |> out.left
            """)).ShouldBe([300f]);

    [Fact]
    public void A_written_list_can_be_read_by_position() =>
        Frequencies(Built("sine(freq: [110, 220][2]) |> out.left")).ShouldBe([220f]);

    [Fact]
    public void Len_counts_a_piped_list() =>
        Frequencies(Built("let n = [1, 2, 3] |> len()\nsine(freq: n) |> out.left")).ShouldBe([3f]);

    [Fact]
    public void A_module_block_after_a_space_is_not_a_position() =>
        StepsExtra.Of(Built("let n = notes() [ A3 C4 ]\nn |> note(note: _) |> sine(freq: _) |> out.left").Nodes
            .Single(n => n.TypeId == "seq.notes")).Count.ShouldBe(2);

    [Fact]
    public void Turning_a_knob_read_by_position_twice_turns_neither_through_the_list()
    {
        const string source = "let fs = [110]\nsine(freq: fs[1]) + saw(freq: fs[1]) |> out.left";

        var load = Try(source);
        var sine = load.Patch.Nodes.Single(n => n.TypeId == "osc.sine");
        var edited = load.Map.Knob(sine.Id, "freq", "55") is { } change
            ? source.Remove(change.Offset, change.Length).Insert(change.Offset, change.Text)
            : source;

        var saw = NodeCatalog.BuiltIn.Require("osc.saw");
        var freq = saw.Inputs.ToList().FindIndex(p => p.Name == "freq");

        Built(edited).Nodes.Single(n => n.TypeId == "osc.saw").InputValues[freq].ShouldBe(110f, edited);
    }

    [Theory]
    [InlineData("let xs = [1, 2]\nsine(freq: xs[3]) |> out.left", IssueCode.BadPosition)]
    [InlineData("let xs = [1, 2]\nsine(freq: xs[0]) |> out.left", IssueCode.BadPosition)]
    [InlineData("let xs = [1, 2]\nsine(freq: xs[1.5]) |> out.left", IssueCode.BadPosition)]
    [InlineData("let xs = [1, 2]\nsine(freq: xs[sine()]) |> out.left", IssueCode.BadPosition)]
    [InlineData("let xs = [1, 2]\nsine(freq: xs[1, 2]) |> out.left", IssueCode.Syntax)]
    [InlineData("let v = 3\nsine(freq: v[1]) |> out.left", IssueCode.NotAList)]
    [InlineData("sine(freq: len(3)) |> out.left", IssueCode.NotAList)]
    public void A_misread_position_is_known_by_its_code(string source, string code) =>
        Try(source).Issues.ShouldContain(issue => issue.Code == code, Try(source).Report);
}
