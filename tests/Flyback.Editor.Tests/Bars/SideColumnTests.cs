using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Bars;
using Flyback.Editor.Composition;
using Flyback.Editor.Windows;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Bars;

/// <summary>
/// The toolbar's side button: the preview and the inspector put away, their width
/// the canvas's, and back at the width they had.
/// </summary>
public class SideColumnTests : EditorTest
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
    public void Swapping_leaves_the_column_away()
    {
        var window = Open();
        var editor = Editor(window);
        var side = Button(window, "side");

        Press(window, side);
        var canvasWas = editor.Bounds.Width;
        Press(window, Button(window, "swap"));

        side.IsChecked.ShouldBe(false, "a swap does not bring the column back");
        side.IsEnabled.ShouldBeTrue();
        ToolTip.GetTip(side).ShouldBe(Toolbar.SideSwappedTip);
        editor.Bounds.Width.ShouldBe(0, 0.5);
        Preview(window).Bounds.Width.ShouldBe(canvasWas, 0.5, "the picture has the canvas's whole width");

        Press(window, Button(window, "swap"));

        side.IsChecked.ShouldBe(false);
        editor.Bounds.Width.ShouldBe(canvasWas, 0.5);
        ToolTip.GetTip(side).ShouldBe(Toolbar.SideTip);
    }

    /// <summary>A window too narrow for two columns keeps the swap: the picture shows, and the side button trades it for the canvas.</summary>
    [AvaloniaFact]
    public void A_narrow_window_keeps_the_swap()
    {
        var window = Open();
        var editor = Editor(window);
        var swap = Button(window, "swap");
        var side = Button(window, "side");

        Press(window, side);
        Press(window, swap);

        window.MinWidth = 0;
        window.Width = 500;
        Settle(window);

        swap.IsChecked.ShouldBe(true, "a small window does not swap back");
        swap.IsEnabled.ShouldBeTrue();
        ToolTip.GetTip(side).ShouldBe(Toolbar.SideNarrowSwappedTip);
        editor.Bounds.Width.ShouldBe(0, 0.5);
        Preview(window).Bounds.Width.ShouldBeGreaterThan(400, "the picture has the one column");

        Press(window, side);

        editor.Bounds.Width.ShouldBeGreaterThan(400, "the side button trades the picture for the canvas");
        Preview(window).Bounds.Width.ShouldBe(0, 0.5);

        Press(window, side);
        window.Width = 1400;
        Settle(window);

        swap.IsChecked.ShouldBe(true);
        side.IsChecked.ShouldBe(false);
        editor.Bounds.Width.ShouldBe(0, 0.5);
        Preview(window).Bounds.Width.ShouldBeGreaterThan(800);
    }

    /// <summary>Swapped, the column holds the canvas, so putting it away gives the picture and the knobs the width.</summary>
    [AvaloniaFact]
    public void Putting_the_column_away_while_swapped_gives_the_picture_its_width_and_back()
    {
        var window = Open();
        var editor = Editor(window);
        var side = Button(window, "side");

        Press(window, Button(window, "controls"));
        Press(window, Button(window, "swap"));
        var pictureWas = Preview(window).Bounds.Width;
        var knobs = All<Flyback.Editor.Knobs.ControlsPanel>(window).Single();

        Press(window, side);

        side.IsChecked.ShouldBe(false);
        editor.Bounds.Width.ShouldBe(0, 0.5);
        Preview(window).Bounds.Width.ShouldBeGreaterThan(pictureWas + 250);
        knobs.IsEffectivelyVisible.ShouldBeTrue("the knobs stay under the picture");
        knobs.Bounds.Width.ShouldBe(Preview(window).Bounds.Width, 1);

        Press(window, side);

        Preview(window).Bounds.Width.ShouldBe(pictureWas, 0.5);
        editor.Bounds.Width.ShouldBeGreaterThan(250);
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
