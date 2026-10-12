using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Composition;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Bars;

/// <summary>A finger held on a glyph reads its tip, which a finger cannot hover for.</summary>
public class TouchTipsTests : EditorTest
{
    private static readonly PointerPointProperties Down = new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
    private static readonly PointerPointProperties Moving = new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
    private static readonly PointerPointProperties Up = new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

    private static T Named<T>(Window window, string name) where T : Control => All<T>(window).Single(b => b.Name == name);

    /// <summary>
    /// A finger on the middle of <paramref name="target"/>, landing on whatever is drawn
    /// there and taken by it, as a touch screen's is. Held long enough when
    /// <paramref name="tips"/> is given, and slid by <paramref name="slide"/> before lifting.
    /// </summary>
    private static void Finger(MainWindow window, Control target, TouchTips? tips, Vector slide = default, bool scrolled = false)
    {
        var at = MiddleOf(target, window);
        var under = (Control)window.InputHitTest(at)!;
        var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);

        finger.Capture(under);
        under.RaiseEvent(new PointerPressedEventArgs(under, finger, window, at, 1_000, Down, KeyModifiers.None));

        if (slide != default)
            under.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, under, finger, window, at + slide, 1_100, Moving, KeyModifiers.None));

        // What a scroll does on taking a finger over: the finger is no longer anything's.
        if (scrolled) finger.Capture(null);

        tips?.Held();

        under.RaiseEvent(new PointerReleasedEventArgs(under, finger, window, at + slide, 1_500, Up, KeyModifiers.None, MouseButton.Left));
        Settle(window);
    }

    [AvaloniaFact]
    public void A_held_finger_opens_the_tip_and_does_not_press_the_button()
    {
        var window = Open();
        var tips = Service<TouchTips>(window);
        var code = Named<ToggleButton>(window, "code");

        Finger(window, code, tips);

        tips.Open.ShouldBe(code);
        ToolTip.GetIsOpen(code).ShouldBeTrue();
        code.IsChecked.ShouldBe(false);
        code.IsPressed.ShouldBeFalse("the button is let go, not left looking held");
    }

    [AvaloniaFact]
    public void A_tap_still_presses_the_button()
    {
        var window = Open();
        var tips = Service<TouchTips>(window);
        var code = Named<ToggleButton>(window, "code");

        Finger(window, code, null);

        code.IsChecked.ShouldBe(true);
        tips.Open.ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_next_press_takes_the_tip_down()
    {
        var window = Open();
        var tips = Service<TouchTips>(window);
        var code = Named<ToggleButton>(window, "code");

        Finger(window, code, tips);
        Finger(window, Named<ToggleButton>(window, "controls"), null);

        tips.Open.ShouldBeNull();
        ToolTip.GetIsOpen(code).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_finger_that_slides_off_opens_nothing()
    {
        var window = Open();
        var tips = Service<TouchTips>(window);
        var code = Named<ToggleButton>(window, "code");

        Finger(window, code, tips, new Vector(FingerSwipe.Slop * 2, 0));

        tips.Open.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_finger_a_scroll_took_over_opens_nothing()
    {
        var window = Open();
        var tips = Service<TouchTips>(window);

        Finger(window, Named<ToggleButton>(window, "code"), tips, scrolled: true);

        tips.Open.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_disabled_button_that_shows_its_tip_shows_it_to_a_finger_too()
    {
        var window = Open();
        var tips = Service<TouchTips>(window);
        var tidy = Named<Button>(window, "tidy");

        tidy.IsEnabled = false;
        Settle(window);

        Finger(window, tidy, tips);

        tips.Open.ShouldBe(tidy);
    }
}
