using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Editor.Bars;
using Flyback.Ui.Controls;
using Flyback.Editor.Windows;
using Shouldly;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Tests;

/// <summary>
/// The transport over the full-screen picture: one bar behind one set of dots, in the
/// toolbar's order, at the top unless the settings give the top to the knobs.
/// </summary>
public sealed class FullScreenTransportTests : EditorTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-transport-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    public override void Dispose()
    {
        base.Dispose();

        if (Path.GetDirectoryName(settingsPath) is { } folder && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static TransportOverlay Overlay(MainWindow window) => All<TransportOverlay>(window).Single();

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static void FullScreen(MainWindow window) => DoubleClickPicture(window);

    /// <summary>The middle of the dots, in the window.</summary>
    private static Point Dots(MainWindow window, TuckedAway overlay)
    {
        var dots = overlay.Dots;

        return dots.TranslatePoint(new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2), window)!.Value;
    }

    /// <summary>Brings the pointer onto the dots, which is what opens them.</summary>
    private static void Reach(MainWindow window, TuckedAway overlay)
    {
        window.MouseMove(Dots(window, overlay));
        Settle(window);
    }

    [AvaloniaFact]
    public void The_transport_waits_behind_one_set_of_dots_at_the_top()
    {
        var window = Open();

        FullScreen(window);

        var overlay = Overlay(window);

        overlay.IsEffectivelyVisible.ShouldBeTrue();
        overlay.VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Top);
        overlay.IsOpen.ShouldBeFalse("it waits behind its dots");
        All<TuckedAway>(window).Where(t => t.IsEffectivelyVisible && t.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Top)
            .ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public void The_transport_is_in_the_toolbars_order()
    {
        var window = Open();

        var tips = All<Control>(Overlay(window))
            .Where(c => c is Button || c is SeekTrack)
            .Select(c => ToolTip.GetTip(c) as string)
            .ToList();

        tips.ShouldBe([
            "Pause or play",
            "Back to the start",
            "Drag to move the patch's clock, in the picture and in the sound.",
            "Play the patch's length round and round",
            "Sound on or off",
        ]);
    }

    /// <summary>The strip is the whole of what it says about time: the picture carries no numbers.</summary>
    [AvaloniaFact]
    public void The_transport_over_the_picture_says_no_time()
    {
        var window = Open();

        FullScreen(window);
        Reach(window, Overlay(window));

        All<TextBlock>(Overlay(window)).Where(t => !string.IsNullOrEmpty(t.Text)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Reaching_the_dots_opens_a_bar_that_moves_the_clock()
    {
        var window = Open();

        FullScreen(window);
        Reach(window, Overlay(window));

        Overlay(window).IsOpen.ShouldBeTrue();

        var track = Overlay(window).Track;
        var at = track.TranslatePoint(track.At(60), window)!.Value;

        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Preview(window).Time.ShouldBe(60, 1);
    }

    /// <summary>The strip opens under the dots, and a click aimed at them is not a seek.</summary>
    [AvaloniaFact]
    public void A_click_on_the_dots_opens_the_bar_and_moves_nothing()
    {
        var window = Open();

        FullScreen(window);
        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        var before = Preview(window).Time;
        var at = Dots(window, Overlay(window));

        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Overlay(window).IsOpen.ShouldBeTrue();
        Preview(window).Time.ShouldBe(before);
    }

    [AvaloniaFact]
    public void Its_loop_switch_is_the_toolbars()
    {
        var window = Open();
        var loop = All<ToggleButton>(window).Single(b => b.Name == "seekLoop");

        FullScreen(window);
        Reach(window, Overlay(window));

        All<Button>(Overlay(window)).Single(b => ToolTip.GetTip(b) as string == "Play the patch's length round and round")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        loop.IsChecked.ShouldBe(true);
        Overlay(window).Looped.ShouldBeTrue();
    }

    /// <summary>On is the knobs' amber and off is the white every other button is, so the two are told apart at a glance.</summary>
    [AvaloniaFact]
    public void The_loop_switch_looks_different_on_and_off()
    {
        var window = Open();

        FullScreen(window);
        Reach(window, Overlay(window));

        var button = All<Button>(Overlay(window)).Single(b => ToolTip.GetTip(b) as string == "Play the patch's length round and round");
        IBrush? Ink() => All<Avalonia.Controls.Presenters.ContentPresenter>(button).First().Foreground;

        // Through the seek bar's own switch: its ticker pushes that state onto the
        // overlay every tenth of a second, and would undo a state set on the overlay alone.
        var loop = Service<SeekBar>(window).Loop;

        loop.IsChecked = false;
        Settle(window);
        var off = (Ink() as ISolidColorBrush).ShouldNotBeNull().Color;

        loop.IsChecked = true;
        Settle(window);
        var on = (Ink() as ISolidColorBrush).ShouldNotBeNull().Color;

        on.ShouldBe(Flyback.Ui.Controls.Colors.Attention);
        off.ShouldNotBe(on);
    }

    [AvaloniaFact]
    public void The_settings_can_give_the_top_to_the_knobs()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        new OutputSettings { Transport = TransportEdge.Bottom }.Save(settingsPath);

        var window = Open(setup: new EditorSetup { Folders = new() { SettingsPath = settingsPath } });

        Overlay(window).VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Bottom);
        All<StageKnobs>(window).Single().VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Top);
    }
}
