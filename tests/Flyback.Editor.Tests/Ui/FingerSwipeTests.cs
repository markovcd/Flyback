using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// A finger on a control in a scrolling panel: up or down over a slider, a level or a cell is
/// the panel's scroll and sideways turns it; a knob holds its finger and turns either way.
/// </summary>
public class FingerSwipeTests : EditorTest
{
    [AvaloniaFact]
    public void A_finger_swiping_up_over_a_knob_turns_it_up()
    {
        var knob = new Knob { Value = 0.5, Width = 44, Height = 44 };
        var window = Show(knob);
        var middle = MiddleOf(knob, window);

        FingerAlong(knob, window, middle, Steps(middle, new Vector(3, -60)));

        knob.Value.ShouldBeGreaterThan(0.5);
    }

    [AvaloniaFact]
    public void A_finger_resting_on_a_knob_leaves_it_where_it_was()
    {
        var knob = new Knob { Value = 0.5, Width = 44, Height = 44 };
        var window = Show(knob);
        var middle = MiddleOf(knob, window);

        FingerAlong(knob, window, middle, middle + new Vector(4, -5));

        knob.Value.ShouldBe(0.5);
    }

    [AvaloniaFact]
    public void A_finger_sliding_right_along_a_knob_turns_it_up()
    {
        var knob = new Knob { Value = 0.5, Width = 44, Height = 44 };
        var window = Show(knob);
        var middle = MiddleOf(knob, window);

        FingerAlong(knob, window, middle, Steps(middle, new Vector(40, 3)));

        knob.Value.ShouldBeGreaterThan(0.5);
    }

    [AvaloniaFact]
    public void A_finger_on_a_sliders_track_leaves_its_value()
    {
        var (window, slider, track) = Slid();

        FingerAlong(track, window, MiddleOf(track, window));

        slider.Value.ShouldBe(0.5);
    }

    [AvaloniaFact]
    public void A_mouse_on_a_sliders_track_still_moves_it()
    {
        var (window, slider, track) = Slid();

        window.MouseDown(MiddleOf(track, window), MouseButton.Left);
        window.MouseUp(MiddleOf(track, window), MouseButton.Left);
        Settle(window);

        slider.Value.ShouldNotBe(0.5);
    }

    /// <summary>A slider whose track a finger is left off, and the track's part below the thumb.</summary>
    private (Window Window, Slider Slider, Control Track) Slid()
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Width = 300 };
        FingerSwipe.LeaveTrack(slider);

        var window = Show(slider);
        var below = slider.GetVisualDescendants().OfType<RepeatButton>().First(b => b.Bounds.Width > 0);

        return (window, slider, below);
    }
}
