using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// The toolbar's side button: the preview and the inspector put away, their width
/// the canvas's, and back at the width they had.
/// </summary>
public class SideColumnTests : UiTest
{
    private static ToggleButton Button(MainWindow window, string name) =>
        All<ToggleButton>(window).Single(b => b.Name == name);

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static void Press(MainWindow window, ToggleButton button)
    {
        button.IsChecked = !button.IsChecked;
        Settle(window);
    }

    [AvaloniaFact]
    public void Putting_the_column_away_gives_the_canvas_its_width_and_back()
    {
        var window = Open();
        var editor = Editor(window);
        var canvasWas = editor.Bounds.Width;

        Button(window, "side").IsChecked.ShouldBe(true, "the column starts out showing");

        Press(window, Button(window, "side"));

        Preview(window).IsEffectivelyVisible.ShouldBeTrue("put away by its width, not hidden");
        Preview(window).Bounds.Width.ShouldBe(0, 0.5);
        editor.Bounds.Width.ShouldBeGreaterThan(canvasWas + 250);

        Press(window, Button(window, "side"));

        editor.Bounds.Width.ShouldBe(canvasWas, 0.5);
    }

    [AvaloniaFact]
    public void Swapping_brings_the_column_back_and_holds_it()
    {
        var window = Open();
        var editor = Editor(window);
        var side = Button(window, "side");

        Press(window, side);
        Press(window, Button(window, "swap"));

        side.IsChecked.ShouldBe(true, "the canvas stands in that column now");
        side.IsEnabled.ShouldBeFalse();
        ToolTip.GetTip(side).ShouldBe(Toolbar.SideSwappedTip);
        editor.Bounds.Width.ShouldBeGreaterThan(250, "the canvas has a column to stand in");

        Press(window, Button(window, "swap"));

        side.IsEnabled.ShouldBeTrue();
        side.IsChecked.ShouldBe(true);
        ToolTip.GetTip(side).ShouldBe(Toolbar.SideTip);
    }

    /// <summary>Full screen takes the preview's column whatever its width, and gives it back as it was.</summary>
    [AvaloniaFact]
    public void Full_screen_with_the_column_away_shows_the_picture_and_puts_it_away_again()
    {
        var window = Open();
        var editor = Editor(window);

        Press(window, Button(window, "side"));
        var canvasWas = editor.Bounds.Width;

        var fullScreen = Service<FullScreenPreview>(window);

        fullScreen.Show(true);
        Settle(window);

        Preview(window).Bounds.Width.ShouldBe(canvasWas, 0.5, "the picture has the width the canvas had");
        editor.IsEffectivelyVisible.ShouldBeFalse();

        fullScreen.Show(false);
        Settle(window);

        Preview(window).Bounds.Width.ShouldBe(0, 0.5);
        editor.Bounds.Width.ShouldBe(canvasWas, 0.5);
        Button(window, "side").IsChecked.ShouldBe(false);
    }
}
