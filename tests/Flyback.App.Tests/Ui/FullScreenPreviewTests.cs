using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The preview taking the whole window and giving it back.
/// </summary>
/// <remarks>
/// What these mostly guard is that the picture is put away and brought back
/// rather than rebuilt. The preview is an OpenGL surface, and the difference
/// between hiding what is around it and moving it somewhere else is invisible in
/// a screenshot and expensive in a running program — so the parent it hangs off
/// is checked as carefully as what is on screen.
/// </remarks>
public class FullScreenPreviewTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        Settle(window);

        return window;
    }

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    /// <summary>
    /// The grid the shell's panels are laid across, canvas to inspector. Found
    /// by name: the toolbar is a grid of three columns too, and counting them
    /// stopped telling the two apart when the module list left the layout.
    /// </summary>
    private static Grid Columns(MainWindow window) =>
        All<Grid>(window).First(g => g.Name == "columns");

    private static List<double> Widths(Grid grid) =>
        [.. grid.ColumnDefinitions.Select(c => c.ActualWidth)];

    /// <summary>The middle of the preview, in window coordinates.</summary>
    private static Point Middle(MainWindow window)
    {
        var preview = Preview(window);
        var centre = new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2);

        return preview.TranslatePoint(centre, window)
            ?? throw new InvalidOperationException("the preview is not in this window");
    }

    private static void DoubleClick(MainWindow window)
    {
        var at = Middle(window);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Settle(window);
    }

    private static void PressEscape(MainWindow window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    // --- going ---------------------------------------------------------------

    [AvaloniaFact]
    public void Double_clicking_the_preview_gives_it_the_window()
    {
        var window = Open();

        Editor(window).IsEffectivelyVisible.ShouldBeTrue("the shell starts on screen");

        DoubleClick(window);

        Preview(window).IsEffectivelyVisible.ShouldBeTrue("the picture is the whole point");
        Editor(window).IsEffectivelyVisible.ShouldBeFalse("and everything else is put away");
    }

    /// <summary>
    /// The one that matters most. Hiding what is around the preview and moving
    /// the preview somewhere else look identical on screen, and only one of them
    /// keeps the GPU context — so the parent is pinned rather than trusted.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_is_never_moved_to_get_there()
    {
        var window = Open();

        var preview = Preview(window);
        var parent = preview.GetVisualParent();

        DoubleClick(window);

        Preview(window).ShouldBeSameAs(preview, "the same surface should still be running");
        preview.GetVisualParent().ShouldBeSameAs(parent, "and hanging off the same thing");

        PressEscape(window);

        preview.GetVisualParent().ShouldBeSameAs(parent);
    }

    // --- and coming back -----------------------------------------------------

    [AvaloniaFact]
    public void Double_clicking_again_puts_everything_back()
    {
        var window = Open();

        DoubleClick(window);
        Editor(window).IsEffectivelyVisible.ShouldBeFalse();

        DoubleClick(window);

        Editor(window).IsEffectivelyVisible.ShouldBeTrue();
        Preview(window).IsEffectivelyVisible.ShouldBeTrue("the preview does not leave with the rest");
    }

    [AvaloniaFact]
    public void Escape_puts_everything_back()
    {
        var window = Open();

        DoubleClick(window);
        Editor(window).IsEffectivelyVisible.ShouldBeFalse();

        PressEscape(window);

        Editor(window).IsEffectivelyVisible.ShouldBeTrue();
        Preview(window).IsEffectivelyVisible.ShouldBeTrue();
    }

    /// <summary>
    /// Escape is the module filter's everywhere else, and nothing else in the
    /// shell should start flinching at it because of this.
    /// </summary>
    [AvaloniaFact]
    public void Escape_does_nothing_when_the_preview_has_not_taken_the_window()
    {
        var window = Open();

        PressEscape(window);

        Editor(window).IsEffectivelyVisible.ShouldBeTrue();
        Preview(window).IsEffectivelyVisible.ShouldBeTrue();
    }

    /// <summary>
    /// The columns the shell is spread across give up their width, and get it
    /// back.
    /// </summary>
    /// <remarks>
    /// Asked of the tracks rather than of the controls standing in them. A hidden
    /// control is never arranged, so its <c>Bounds</c> keep whatever they last
    /// were and would answer this question with a stale yes; a column's
    /// <c>ActualWidth</c> is what the grid actually decided this time round.
    /// <para>
    /// And measured rather than compared by identity: handing a grid back the
    /// same definition objects it had is not the same as those objects still
    /// deciding anything, and the difference between the two is the whole of
    /// whether the shell reappears.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void The_layout_that_comes_back_is_the_one_that_went_away()
    {
        var window = Open();
        var columns = Columns(window);

        var before = Widths(columns);
        before.Count(w => w > 1).ShouldBeGreaterThan(1, "the shell starts spread across several columns");

        DoubleClick(window);

        Widths(columns).Count(w => w > 1)
            .ShouldBe(1, "only the preview's column should be left with any width");

        PressEscape(window);

        foreach (var (was, now) in before.Zip(Widths(columns)))
            now.ShouldBe(was, 0.5, "every column should be back to the width it had");
    }

    /// <summary>
    /// A minimum outranks a width of zero, so a track has to have its own put
    /// aside as well — and given back, or the panels would come back unable to be
    /// dragged as narrow as they were before.
    /// </summary>
    [AvaloniaFact]
    public void The_minimum_widths_come_back_too()
    {
        var window = Open();

        var columns = All<Grid>(window)
            .First(g => g.ColumnDefinitions.Count > 1 && g.ColumnDefinitions.Any(c => c.MinWidth > 0));

        var before = columns.ColumnDefinitions.Select(c => c.MinWidth).ToList();

        DoubleClick(window);
        columns.ColumnDefinitions.Select(c => c.MinWidth)
            .ShouldAllBe(m => m == 0, "a minimum would hold a collapsed column open");

        PressEscape(window);
        columns.ColumnDefinitions.Select(c => c.MinWidth).ShouldBe(before);
    }

    /// <summary>
    /// Going full screen twice over is not two states to come back from — the
    /// second is ignored, or Escape would leave the shell half put away.
    /// </summary>
    [AvaloniaFact]
    public void Asking_twice_for_what_is_already_on_changes_nothing()
    {
        var window = Open();

        var editor = Editor(window);

        DoubleClick(window);
        var columnsWhileFull = All<Grid>(window).Select(g => g.ColumnDefinitions).ToList();

        // A second request while already full screen must not overwrite what was
        // saved to come back to.
        All<Grid>(window).Select(g => g.ColumnDefinitions).ShouldBe(columnsWhileFull);

        PressEscape(window);
        editor.IsEffectivelyVisible.ShouldBeTrue();
    }
}
