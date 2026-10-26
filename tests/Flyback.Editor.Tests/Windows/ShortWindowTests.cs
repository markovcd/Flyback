using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flyback.Editor.Bars;
using Flyback.Editor.Shell;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// A page shorter than the panels' least heights, as a phone held sideways gives the web
/// editor: the panels are cut short rather than drawn over the bars along the bottom.
/// </summary>
public sealed class ShortWindowTests : EditorTest
{
    [AvaloniaTheory]
    [InlineData("transport")]
    [InlineData("status")]
    public void The_bars_along_the_bottom_stay_on_top_of_a_short_page(string bar)
    {
        var window = Open(setup: new EditorSetup { Host = new() { InPage = true } });

        window.MinHeight = 0;
        window.Width = 770;
        window.Height = 300;
        Settle(window);

        // A hit test reads the tree as last drawn.
        window.CaptureRenderedFrame()?.Dispose();

        Control view = bar == "transport" ? Service<TransportRow>(window).View : Service<StatusBar>(window).View;
        var hit = window.InputHitTest(MiddleOf(view, window)) as Visual;

        hit.ShouldNotBeNull();
        (hit == view || view.IsVisualAncestorOf(hit)).ShouldBeTrue($"{bar}'s middle is covered by {hit.GetType().Name}");
    }
}
