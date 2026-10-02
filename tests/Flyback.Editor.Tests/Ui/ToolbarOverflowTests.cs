using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Bars;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// The toolbar keeps one row: what does not fit folds into the menu at its end, the
/// program's own buttons first, and comes back as the window widens.
/// </summary>
public sealed class ToolbarOverflowTests : EditorTest
{
    private string?[] Folded(MainWindow window) =>
        Service<Toolbar>(window).Overflow.Folded.Select(control => control.Name).ToArray();

    private static void Resize(MainWindow window, double width)
    {
        window.Width = width;
        Settle(window);
    }

    [AvaloniaFact]
    public void A_wide_window_folds_nothing()
    {
        var window = Open();

        Resize(window, 1440);

        Folded(window).ShouldBeEmpty();
        Service<Toolbar>(window).Overflow.More.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_window_a_little_too_narrow_folds_the_programs_buttons_before_the_patchs()
    {
        var window = Open();

        Resize(window, 600);

        var folded = Folded(window);

        folded.ShouldContain("settings");
        folded.ShouldContain("plugins");
        folded.ShouldContain("about");
        folded.ShouldNotContain("open");
        folded.ShouldNotContain("code");
    }

    [AvaloniaFact]
    public void What_stays_on_the_bar_fits_the_window_in_one_row()
    {
        var window = Open();

        foreach (var width in new[] { 390d, 600, 900 })
        {
            Resize(window, width);

            var bar = Service<Toolbar>(window).View;
            var shown = All<Button>(window).Where(b => b.IsEffectivelyVisible && bar.IsVisualAncestorOf(b)).ToList();

            shown.ShouldAllBe(b => b.TranslatePoint(new Point(b.Bounds.Width, 0), window)!.Value.X <= width + 0.5);
            shown.Select(b => Math.Round(b.TranslatePoint(default, bar)!.Value.Y)).Distinct().Count().ShouldBe(1);
        }
    }

    [AvaloniaFact]
    public void Widening_the_window_brings_the_folded_buttons_back()
    {
        var window = Open();

        Resize(window, 390);
        Folded(window).ShouldNotBeEmpty();

        Resize(window, 1440);

        Folded(window).ShouldBeEmpty();
        All<Button>(window).Single(b => b.Name == "about").IsEffectivelyVisible.ShouldBeTrue();
    }
}
