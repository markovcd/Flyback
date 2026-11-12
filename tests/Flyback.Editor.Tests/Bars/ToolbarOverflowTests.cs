using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flyback.Editor.Bars;
using Flyback.Editor.Composition;
using Flyback.Editor.Shell;
using Shouldly;

namespace Flyback.Editor.Tests.Bars;

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

        Resize(window, 900);

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
            // A button with a legend stands taller than one without, so the row lines up their middles.
            shown.Select(b => Math.Round(b.TranslatePoint(new Point(0, b.Bounds.Height / 2), bar)!.Value.Y)).Distinct().Count().ShouldBe(1);
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

    [AvaloniaFact]
    public void A_wide_window_prints_the_legends_under_the_buttons()
    {
        var window = Open();

        Resize(window, 1440);

        Service<Toolbar>(window).Overflow.LegendsShown.ShouldBeTrue();
        All<TextBlock>(window).ShouldContain(text => text.Text == "SAVE" && text.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_narrowing_window_folds_buttons_and_keeps_the_legends()
    {
        var window = Open();

        Resize(window, 1440);
        Resize(window, 900);

        Service<Toolbar>(window).Overflow.LegendsShown.ShouldBeTrue();
        Folded(window).ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public void A_phone_wide_window_prints_the_legends()
    {
        var window = Open();

        Resize(window, 390);

        Service<Toolbar>(window).Overflow.LegendsShown.ShouldBeTrue();
        All<TextBlock>(window).ShouldContain(text => text.Text == "PRESETS" && text.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Without_legends_a_narrow_window_folds_fewer_buttons()
    {
        var window = Open();
        var overflow = Service<Toolbar>(window).Overflow;

        Resize(window, 390);
        var labeled = Folded(window).Length;

        overflow.LegendsWanted = false;
        overflow.Fit(390);

        overflow.LegendsShown.ShouldBeFalse();
        Folded(window).Length.ShouldBeLessThan(labeled);
    }

    [AvaloniaFact]
    public void A_menu_opened_on_a_narrow_window_lists_only_what_a_wider_one_folds()
    {
        var window = Open(setup: new EditorSetup { Host = new() { Place = EditorPlace.Page } });
        var overflow = Service<Toolbar>(window).Overflow;
        var menu = overflow.More.Flyout.ShouldBeOfType<MenuFlyout>();

        Resize(window, 412);
        menu.ShowAt(overflow.More);
        Settle(window);
        menu.Hide();
        Resize(window, 760);
        menu.ShowAt(overflow.More);
        Settle(window);

        var shown = All<MenuItem>(window).Where(item => item.IsEffectivelyVisible).Select(item => item.Tag).ToHashSet();

        shown.SetEquals(overflow.Folded).ShouldBeTrue();
        overflow.Folded.ShouldNotContain(control => control.Name == "code");
    }
}
