using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Assist;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Assist;

public sealed class AssistantPanelFoldingTests : AssistantPanelTest
{
    [AvaloniaFact]
    public void A_block_too_long_to_read_on_the_way_past_arrives_folded()
    {
        var (window, panel) = Over(new Patch());
        var machinery = Machinery();

        panel.Open(Saved(new TranscriptLine(Voice.Note, machinery)));
        Settle(window);

        Block(window, machinery).IsVisible.ShouldBeFalse();
        Gist(Fold(window)).ShouldBe("Outrun: a whole synthwave track. · 41 lines");
        Opened(Fold(window)).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_folded_block_opens_and_closes_on_its_header()
    {
        var (window, panel) = Over(new Patch());
        var machinery = Machinery();

        panel.Open(Saved(new TranscriptLine(Voice.Note, machinery)));
        Settle(window);

        Press(Fold(window));
        Settle(window);

        Block(window, machinery).IsVisible.ShouldBeTrue();
        Opened(Fold(window)).ShouldBeTrue();

        Press(Fold(window));
        Settle(window);

        Block(window, machinery).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_block_short_enough_to_read_is_left_alone()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(new TranscriptLine(Voice.Note, "Rendered one frame.")));
        Settle(window);

        All<Button>(window).ShouldNotContain(b => b.Name == "fold");
        Block(window, "Rendered one frame.").IsVisible.ShouldBeTrue();
    }

    /// <summary>
    /// The proposal is what the turn was for, so however long it runs it is read
    /// rather than offered.
    /// </summary>
    [AvaloniaFact]
    public void What_the_assistant_proposed_is_never_folded()
    {
        var (window, panel) = Over(new Patch());
        var proposal = Machinery();

        panel.Open(Saved(new TranscriptLine(Voice.Proposed, proposal)));
        Settle(window);

        All<Button>(window).ShouldNotContain(b => b.Name == "fold");
        Block(window, proposal).IsVisible.ShouldBeTrue();
    }
}
