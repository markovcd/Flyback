using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>The Song's parts, written as a block of named lines, read back, and read by name.</summary>
public class SongBlockTests
{
    private static LanguageLoad Read(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static NodeInstance Song(LanguageLoad load) => load.Patch.Nodes.Single(n => n.TypeId == SongModule.TypeId);

    private static List<SongPart> Parts(LanguageLoad load) => SongExtra.Of(Song(load));

    [Fact]
    public void Each_name_starts_a_part_of_its_own_length()
    {
        var parts = Parts(Read("let song = song() [\n  kick: 1 0 >0.5\n  bass: 0 1\n     1 1 ]\nsong.kick |> out.left"));

        parts.Select(part => part.Name).ShouldBe(["kick", "bass"]);
        parts[0].Levels.ShouldBe([new PartLevel(1f), new PartLevel(0f), new PartLevel(0.5f, Glides: true)]);
        parts[1].Levels.Count.ShouldBe(4);
    }

    [Fact]
    public void A_part_is_read_by_its_name()
    {
        var load = Read("let song = song() [ kick: 1 bass: 0 ]\nsong.bass |> out.left");

        var wire = load.Patch.Connections.Single(c => c.SourceNode == Song(load).Id);

        wire.SourcePort.ShouldBe(1);
    }

    [Fact]
    public void Printed_and_read_back_it_is_the_same_song()
    {
        var row = string.Join(' ', Enumerable.Repeat("0.35", 20));
        var load = Read($"let song = song(rate: 0.25) [ kick: {row} bass: 1 >0 ]\nsong.bass + song.kick |> out.left");

        var printed = SourceLayout.Wrap(PatchPrinter.Print(load.Patch, NodeCatalog.BuiltIn));

        printed.ShouldContain("song.bass");
        printed.ShouldContain("    bass: 1 >0");
        Parts(Read(printed)).Select(part => (part.Name, string.Join(' ', part.Levels))).ShouldBe(
            Parts(load).Select(part => (part.Name, string.Join(' ', part.Levels))));
    }

    [Fact]
    public void A_patch_has_one_Song()
    {
        var load = PatchLanguage.Build("song() [ a: 1 ] |> out.left\nsong() [ b: 1 ] |> out.right", NodeCatalog.BuiltIn);

        load.Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.OneSong);
        load.Issues[0].Line.ShouldBe(2);
    }

    [Fact]
    public void Levels_before_any_name_are_refused()
    {
        var load = PatchLanguage.Build("song() [ 1 0 ] |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.PartName);
    }

    [Fact]
    public void Two_parts_of_one_name_are_refused()
    {
        var load = PatchLanguage.Build("song() [ a: 1 A: 0 ] |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.PartName);
    }

    [Fact]
    public void An_output_it_lacks_is_said_with_the_parts_it_has()
    {
        var load = PatchLanguage.Build("let song = song() [ kick: 1 ]\nsong.snare |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldHaveSingleItem().Message.ShouldContain("'kick', 'part_2'");
    }

    [Fact]
    public void Every_part_is_printed_by_its_name_the_first_as_much_as_the_rest()
    {
        var load = Read("let song = song() [ kick: 1 bass: 0 ]\nsong.kick + song.bass |> out.left");

        var printed = PatchPrinter.Print(load.Patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("song.kick");
        printed.ShouldContain("song.bass");
    }
}
