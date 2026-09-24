using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Viewer;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The line in the corner of a full-screen picture saying how it is drawn: off until
/// F3 or <c>--stats</c> asks for it, over the editor's full-screen picture and the
/// viewer's, and nowhere else.
/// </summary>
public class StatsOverlayTests : UiTest
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
    private static void FullScreen(MainWindow window)
    {
        var preview = All<PreviewHost>(window).Single();
        var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

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
        stats.Said.ShouldContain("320×180 · CPU");
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
        Stats(window).Said.ShouldContain("ops");
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

/// <summary>What the stats line says.</summary>
public class StatsLineTests
{
    [Fact]
    public void The_line_reads_rate_cost_ops_size_renderer_and_clock() =>
        StatsOverlay.Line(59.6, 4.26, 29, new PixelSize(960, 540), PreviewBackend.Gpu, 65.25)
            .ShouldBe("60 fps · 4.3 ms · 29 ops · 960×540 · GPU · t 1:05.25");
}
