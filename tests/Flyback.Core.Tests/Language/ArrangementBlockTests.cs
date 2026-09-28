using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>An Arrangement's parts, written as a block and read back.</summary>
public class ArrangementBlockTests
{
    private static LanguageLoad Read(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static List<List<PartLevel>> Parts(LanguageLoad load) =>
        ArrangementExtra.Of(load.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.ArrangementTypeId));

    [Fact]
    public void A_block_is_a_row_of_levels_for_each_part()
    {
        var parts = Parts(Read("arrangement() [ 1 ~ >0.5 | 0 1 1 ] |> out.left"));

        parts.Count.ShouldBe(2);
        parts[0].ShouldBe([new PartLevel(1f), new PartLevel(0f), new PartLevel(0.5f, Glides: true)]);
        parts[1].Select(l => l.Value).ShouldBe([0f, 1f, 1f]);
    }

    [Fact]
    public void A_part_may_run_over_several_lines()
    {
        var parts = Parts(Read("arrangement() [\n  1 1\n  0 0 |\n  1 ] |> out.left"));

        parts.Select(p => p.Count).ShouldBe([4, 1]);
    }

    [Fact]
    public void Printed_and_read_back_it_is_the_same_arrangement()
    {
        var load = Read("let song = arrangement(rate: 0.25) [ 1 0 >1 0.5 | -0.4 0.25 0 12 ]\nsong |> out.left");

        var printed = PatchPrinter.Print(load.Patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("[ 1 0 >1 0.5 | -0.4 0.25 0 12 ]");
        Parts(Read(printed)).ShouldBe(Parts(load), ignoreOrder: false);
    }

    [Fact]
    public void A_long_arrangement_is_printed_a_part_a_line()
    {
        var row = string.Join(' ', Enumerable.Repeat("0.35", 12));
        var load = Read($"let song = arrangement() [ {row} | {row} | {row} ]\nsong |> out.left");

        var printed = SourceLayout.Wrap(PatchPrinter.Print(load.Patch, NodeCatalog.BuiltIn));

        printed.Split('\n').Count(line => line.Trim().StartsWith("0.35")).ShouldBe(3);
    }

    [Fact]
    public void Something_that_is_not_a_level_is_said_where_it_stands()
    {
        var load = PatchLanguage.Build("arrangement() [ 1 x ] |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.LevelSyntax);
        load.Issues[0].Column.ShouldBe(19);
    }

    [Fact]
    public void Nine_parts_are_one_too_many()
    {
        var load = PatchLanguage.Build(
            "arrangement() [ " + string.Join(" | ", Enumerable.Repeat("1", 9)) + " ] |> out.left",
            NodeCatalog.BuiltIn);

        load.Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.TooManyParts);
    }

    [Fact]
    public void Parts_are_changed_where_the_block_stands()
    {
        const string source = "let song = arrangement() [ 1 0 ]\nsong |> out.left";

        var load = Read(source);
        var node = load.Patch.Nodes.Single(n => n.Name == "song").Id;
        var change = load.Map.Carried(node, "[ 0 1 | 1 1 ]")!.Value;

        (source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..])
            .ShouldBe("let song = arrangement() [ 0 1 | 1 1 ]\nsong |> out.left");
    }
}
