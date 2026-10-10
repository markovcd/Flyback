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
    // --- the working folds away -----------------------------------------------

    private static List<Button> Runs(Window window) => [.. All<Button>(window).Where(b => b.Name == "steps")];

    private static string Tally(Button run) => Parts(run).OfType<TextBlock>().Single(t => t.Name == "tally").Text ?? string.Empty;

    private static readonly TranscriptLine[] Worked =
    [
        new(Voice.You, "wire the room in"),
        new(Voice.Said, "Wiring it in."),
        new(Voice.Note, "added blipecho"),
        new(Voice.Note, "wired blip.out -> blipecho.in"),
        new(Voice.Aside, "75910 in, 3833 out"),
        new(Voice.Said, "Done."),
    ];

    [AvaloniaFact]
    public void What_it_did_between_two_things_it_said_is_one_line_with_a_count()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(Worked));
        Settle(window);

        var run = Runs(window)[0];

        Tally(run).ShouldBe("3 steps");
        Opened(run).ShouldBeFalse();
        All<SelectableTextBlock>(window).Single(b => b.Text == "added blipecho").GetVisualAncestors().OfType<StepsGroup>().ShouldNotBeEmpty();
        Block(window, "Done.").GetVisualAncestors().OfType<StepsGroup>().ShouldBeEmpty();
        Block(window, "Wiring it in.").GetVisualAncestors().OfType<StepsGroup>().ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void The_line_opens_and_closes_what_it_holds()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(Worked));
        Settle(window);

        var run = Runs(window)[0];

        Press(run);
        Settle(window);

        Opened(run).ShouldBeTrue();

        Press(run);
        Settle(window);

        Opened(run).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void What_it_is_doing_now_stays_open_until_it_speaks_again()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(Worked[..^1]));
        Settle(window);

        Opened(Runs(window).Single()).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_failure_ends_the_run_and_is_not_folded_into_it()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(
            new TranscriptLine(Voice.Note, "wired a -> b"),
            new TranscriptLine(Voice.Failed, "the provider refused"),
            new TranscriptLine(Voice.Note, "wired c -> d")));
        Settle(window);

        Runs(window).Count.ShouldBe(2);
        Opened(Runs(window)[0]).ShouldBeFalse();
        Block(window, "the provider refused").GetVisualAncestors().OfType<StepsGroup>().ShouldBeEmpty();
    }
}
