using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Viewer.Desktop;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// The line in the corner of a full-screen picture saying how it is drawn: off until
/// F3 or <c>--stats</c> asks for it, over the editor's full-screen picture and the
/// viewer's, and nowhere else.
/// </summary>
public class StatsOverlayTests : EditorTest
{
    private static ViewerOptions Options() => new() { Gpu = false, Size = new PixelSize(320, 180) };

    private static Opened Plasma() => new(
        Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn), new SampleLibrary(), new ImageLibrary());

    private static void PressF3(Avalonia.Controls.Window window)
    {
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Settle(window);
    }

    /// <summary>The editor's picture given the whole window, as a double-click on it does.</summary>
    private static void FullScreen(MainWindow window) => DoubleClickPicture(window);

    private static StatsOverlay Stats(Avalonia.Controls.Window window) => All<StatsOverlay>(window).Single();

    private ViewerWindow Viewer(ViewerOptions options)
    {
        var window = Owned(ViewerServices.Window(new ViewerLaunch(Plasma(), null, options)));

        window.Show();
        Settle(window);

        return window;
    }

    [AvaloniaFact]
    public void The_viewer_shows_no_stats_unless_asked()
    {
        Stats(Viewer(Options())).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_viewer_opens_with_its_stats_showing_when_asked()
    {
        var stats = Stats(Viewer(Options() with { Stats = true }));

        stats.IsVisible.ShouldBeTrue();
        stats.Said.ShouldContain("fps");
        stats.Said.ShouldContain(" ms · 320×180 · CPU", Case.Sensitive, "Plasma has no sound, so no oversampling is said");
        stats.Said.ShouldNotContain("ops", Case.Insensitive, "only the editor counts ops");
    }

    [AvaloniaFact]
    public void F3_shows_the_viewers_stats_and_puts_them_away()
    {
        var window = Viewer(Options());

        PressF3(window);
        Stats(window).IsVisible.ShouldBeTrue();

        PressF3(window);
        Stats(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_run_with_no_picture_has_no_stats()
    {
        All<StatsOverlay>(Viewer(Options() with { NoVideo = true })).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void F3_over_the_editors_full_screen_picture_shows_its_stats()
    {
        var window = Open();

        FullScreen(window);
        PressF3(window);

        Stats(window).IsEffectivelyVisible.ShouldBeTrue();
        Stats(window).Said.ShouldNotContain("ops");
    }

    [AvaloniaFact]
    public void The_editors_stats_go_with_the_full_screen_and_come_back_with_it()
    {
        var window = Open();

        FullScreen(window);
        PressF3(window);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);

        Stats(window).IsVisible.ShouldBeFalse("the status bar says it all while the editor is showing");

        FullScreen(window);

        Stats(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void F3_in_the_editor_is_nothing_while_the_picture_is_in_its_place()
    {
        var window = Open();

        PressF3(window);

        Stats(window).IsVisible.ShouldBeFalse();
    }
}
