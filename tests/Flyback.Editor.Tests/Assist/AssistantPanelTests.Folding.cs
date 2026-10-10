using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Flyback.Assist;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

public partial class AssistantPanelTests
{
    // --- long blocks fold -----------------------------------------------------

    /// <summary>
    /// What a tool answers with: a caption, and then the patch written out, which
    /// is the length that made the transcript unreadable.
    /// </summary>
    private static string Machinery() => "Outrun: a whole synthwave track.\n"
        + string.Join('\n', Enumerable.Range(0, 40).Select(index => $"let value{index} = x |> value(v: {index})"));

    private static Button Fold(Window window) => All<Button>(window).Single(b => b.Name == "fold");

    /// <summary>What a fold says of its block, without the arrow. Read off its content, which a hidden fold never lays out.</summary>
    private static string Gist(Button fold) => Parts(fold).OfType<TextBlock>().Single(t => t.Name == "gist").Text ?? string.Empty;

    /// <summary>Whether a fold's arrow points down, at an open block.</summary>
    private static bool Opened(Button fold) => Parts(fold).Any(c => c.Name == "open");

    private static IEnumerable<Control> Parts(Button fold) => (fold.Content as Panel)?.Children ?? [];

    private static SelectableTextBlock Block(Window window, string text) =>
        All<SelectableTextBlock>(window).Single(block => block.Text == text);

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
