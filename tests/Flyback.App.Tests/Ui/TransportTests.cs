using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Play and pause in the editor, on the toolbar and over the full-screen preview.
/// </summary>
public class TransportTests : UiTest
{
    private MainWindow Open()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        return window;
    }

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static Button Pause(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "pause");

    private static TransportOverlay Overlay(MainWindow window) => All<TransportOverlay>(window).Single();

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>The overlay's own button, found by what its tip says.</summary>
    private static Button Tool(MainWindow window, string tip) =>
        All<Button>(Overlay(window)).Single(b => ToolTip.GetTip(b) as string == tip);

    private static void FullScreen(MainWindow window)
    {
        var preview = Preview(window);
        var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Settle(window);
    }

    [AvaloniaFact]
    public void Pausing_holds_the_picture_on_a_clock_that_does_not_move()
    {
        var window = Open();
        var preview = Preview(window);

        Click(Pause(window));
        Settle(window);

        window.Paused.ShouldBeTrue();
        preview.Clock.ShouldNotBeNull();

        var held = preview.Clock!();

        Settle(window);

        preview.Clock!().ShouldBe(held);
    }

    [AvaloniaFact]
    public void Ctrl_P_pauses_and_plays_on()
    {
        var window = Open();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        window.Paused.ShouldBeTrue();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        window.Paused.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_button_says_what_a_press_does_next()
    {
        var window = Open();
        var pause = Pause(window);

        var whenPlaying = ToolTip.GetTip(pause);
        var glyph = pause.Content;

        Click(pause);

        ToolTip.GetTip(pause).ShouldNotBe(whenPlaying);
        pause.Content.ShouldNotBeSameAs(glyph);

        Click(pause);

        window.Paused.ShouldBeFalse();
        ToolTip.GetTip(pause).ShouldBe(whenPlaying);
    }

    [AvaloniaFact]
    public void Rewinding_while_paused_stays_paused_on_the_first_frame()
    {
        var window = Open();
        var preview = Preview(window);

        preview.Time = 5;
        Click(Pause(window));

        Click(All<Button>(window).Single(b => b.Name == "rewind"));
        Settle(window);

        window.Paused.ShouldBeTrue();
        preview.Time.ShouldBe(0);
        preview.Clock!().ShouldBe(0);
    }

    [AvaloniaFact]
    public void The_controls_are_over_the_picture_only_while_it_has_the_window()
    {
        var window = Open();

        Overlay(window).IsEffectivelyVisible.ShouldBeFalse();

        FullScreen(window);

        Overlay(window).IsEffectivelyVisible.ShouldBeTrue();
        Pause(window).IsEffectivelyVisible.ShouldBeFalse("the toolbar is put away with the rest of the shell");

        FullScreen(window);

        Overlay(window).IsEffectivelyVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_full_screen_pause_is_the_toolbars_pause()
    {
        var window = Open();

        FullScreen(window);

        Click(Tool(window, "Pause or play"));

        window.Paused.ShouldBeTrue();
        Preview(window).Clock.ShouldNotBeNull();

        FullScreen(window);

        window.Paused.ShouldBeTrue("pausing outlives the full screen");

        Click(Pause(window));

        window.Paused.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_full_screen_sound_button_turns_the_sound_off_and_on()
    {
        var window = Open();

        FullScreen(window);

        Click(Tool(window, "Sound on or off"));

        window.Muted.ShouldBeTrue();

        Click(Tool(window, "Sound on or off"));

        window.Muted.ShouldBeFalse();
    }
}
