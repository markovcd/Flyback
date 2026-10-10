using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Settings;

/// <summary>A page's settings (ADR-0182): a panel off the gear whose switches apply and are kept in the browser at once.</summary>
public sealed class PageSettingsTests : EditorTest
{
    /// <summary>A browser's local storage, kept across the editors a test opens.</summary>
    private sealed class Browser : IBrowserStore
    {
        public Dictionary<string, string> Kept { get; } = [];

        public string? Read(string section) => Kept.GetValueOrDefault(section);

        public void Write(string section, string json) => Kept[section] = json;
    }

    private readonly Browser browser = new();

    private MainWindow Open()
    {
        var window = NewMainWindow(
            new EditorSetup { Host = new() { InPage = true } },
            services => services.AddSingleton<IBrowserStore>(browser));

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private PageSettings Panel(MainWindow window) => Service<PageSettings>(window);

    private static void PressGear(MainWindow window)
    {
        Press(All<Button>(window).Single(b => b.Name == "settings"));
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    private CheckBox Box(MainWindow window, string name) =>
        ((Control)Panel(window).Flyout.Content!).GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void The_gear_opens_the_panel_and_no_settings_window()
    {
        var window = Open();

        All<Button>(window).Single(b => b.Name == "settings").IsVisible.ShouldBeTrue();

        PressGear(window);

        Panel(window).Flyout.IsOpen.ShouldBeTrue();
        All<ModalOverlay>(window).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Ticking_drag_to_pan_applies_at_once_and_is_kept_for_the_next_visit()
    {
        var window = Open();
        PressGear(window);

        Box(window, "pageDragToPan").IsChecked = true;
        Settle(window);

        Editor(window).Gestures.DragToPan.ShouldBeTrue();
        All<TextBlock>(window).Any(t => t.Text == "Right-drag").ShouldBeTrue("the help names the gestures in use");

        var next = Open();
        Editor(next).Gestures.DragToPan.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Ticking_compact_modules_draws_them_compact()
    {
        var window = Open();
        PressGear(window);

        Box(window, "pageCompactModules").IsChecked = true;

        Editor(window).Geometry.Compact.ShouldBeTrue();
        CanvasSettingsOf(browser).CompactModules.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_finger_on_the_canvas_takes_drag_to_pan_off_the_panel()
    {
        var window = Open();

        Service<Reactions>(window).Raise(new Touched());
        PressGear(window);

        ((Control)Panel(window).Flyout.Content!).GetLogicalDescendants().OfType<Border>()
            .Single(b => b.Name == "pageDragToPanRow").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_browser_that_kept_nothing_opens_with_the_defaults()
    {
        var window = Open();

        Editor(window).Gestures.DragToPan.ShouldBeFalse();
        Editor(window).Geometry.Compact.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"DragToPan":"yes","CompactModules":7}""")]
    public void A_browser_that_kept_garbage_opens_with_the_defaults_and_is_written_over(string kept)
    {
        browser.Kept[Flyback.Editor.Canvas.CanvasSettings.Section] = kept;

        var window = Open();

        Editor(window).Gestures.DragToPan.ShouldBeFalse();
        Editor(window).Geometry.Compact.ShouldBeFalse();

        PressGear(window);
        Box(window, "pageDragToPan").IsChecked = true;

        CanvasSettingsOf(browser).DragToPan.ShouldBeTrue();
    }

    private static Flyback.Editor.Canvas.CanvasSettings CanvasSettingsOf(Browser browser) =>
        Flyback.Editor.Canvas.CanvasSettings.Parse(browser.Kept[Flyback.Editor.Canvas.CanvasSettings.Section]);
}
