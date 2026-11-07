using Flyback.Core.Graph;
using Flyback.Editor.Sequence;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Sequence;

/// <summary>The edits the Sequence pane makes to the Song's parts.</summary>
public class SongPartsTests
{
    private static SongParts Two() => new([new("kick", [new(1f), new(0f)]), new("bass", [new(0.5f)])]);

    [Fact]
    public void A_click_switches_a_level_off_and_back_on_at_the_part_s_strongest()
    {
        var parts = Two();

        parts.Toggle(0, 0);
        parts.Levels(0)[0].Value.ShouldBe(0f);

        parts.Toggle(1, 0);
        parts.Toggle(1, 0);
        parts.Levels(1)[0].Value.ShouldBe(1f);
    }

    [Fact]
    public void A_longer_part_gets_new_sections_at_nought_and_a_shorter_one_loses_its_last()
    {
        var parts = Two();

        parts.Resize(1, 3);
        parts.Levels(1).Select(level => level.Value).ShouldBe([0.5f, 0f, 0f]);

        parts.Resize(0, 0);
        parts.Levels(0).Count.ShouldBe(1);
        parts.Longest.ShouldBe(3);
    }

    [Fact]
    public void Painting_gives_a_run_of_sections_one_level()
    {
        var parts = new SongParts([new("pad", [new(0f), new(0f), new(0f), new(0f)])]);

        parts.Paint(0, 3, 1, new PartLevel(0.7f, Glides: true));

        parts.Levels(0).Select(level => level.Value).ShouldBe([0f, 0.7f, 0.7f, 0.7f]);
    }

    [Fact]
    public void A_name_the_text_cannot_write_or_another_part_has_is_refused()
    {
        var parts = Two();

        parts.Rename(1, "kick drum").ShouldBeFalse();
        parts.Rename(1, "KICK").ShouldBeFalse();
        parts.Rename(1, " sub ").ShouldBeTrue();
        parts.Name(1).ShouldBe("sub");
    }

    [Fact]
    public void A_part_moves_duplicates_and_goes()
    {
        var parts = Two();

        parts.Move(1, -1);
        parts.Duplicate(0);

        parts.Parts.Select(part => part.Name).ShouldBe(["bass", "bass2", "kick"]);

        parts.Remove(1);
        parts.Add();

        parts.Parts.Select(part => part.Name).ShouldBe(["bass", "kick", "part3"]);
        parts.Levels(2).Count.ShouldBe(2);
    }

    [Fact]
    public void No_more_parts_are_added_than_the_Song_has_outputs_for()
    {
        var parts = Two();

        for (var i = 0; i < 20; i++) parts.Add();

        parts.Count.ShouldBe(NodeCatalog.MaxParts);
    }
}
