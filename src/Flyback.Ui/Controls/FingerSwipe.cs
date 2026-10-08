using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Flyback.Ui.Controls;

/// <summary>
/// Whether a finger pressed on a control inside a scrolling panel means the panel or the
/// control: once it has moved past <see cref="Slop"/>, up or down scrolls and sideways turns.
/// </summary>
internal sealed class FingerSwipe(Point from)
{
    /// <summary>How far a finger travels, in screen pixels, before it is moving rather than resting.</summary>
    public const double Slop = 10;

    /// <summary>Null until the finger has gone past the slop; then whether it turns the control.</summary>
    public bool? Turns { get; private set; }

    /// <summary>Where the finger came down.</summary>
    public Point From { get; } = from;

    /// <summary>Decides, once, from where the finger is now, and answers <see cref="Turns"/>.</summary>
    public bool? Read(Point at)
    {
        if (Turns is not null) return Turns;

        var moved = at - From;
        if (Math.Max(Math.Abs(moved.X), Math.Abs(moved.Y)) < Slop) return null;

        return Turns = Math.Abs(moved.X) > Math.Abs(moved.Y);
    }

    /// <summary>Whether a finger lifted at <paramref name="at"/> was a tap.</summary>
    public bool Tapped(Point at) => Turns is null && Point.Distance(From, at) < Slop;

    /// <summary>
    /// Leaves a slider's track to the panel's scroll under a finger, so a swipe that starts on it
    /// does not move the value; the thumb still drags.
    /// </summary>
    public static void LeaveTrack(Slider slider) =>
        slider.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) =>
            {
                if (e.Pointer.Type == PointerType.Touch && !OnThumb(e.Source)) e.Handled = true;
            },
            RoutingStrategies.Tunnel);

    private static bool OnThumb(object? source) =>
        source is Visual visual && (visual is Thumb || visual.FindAncestorOfType<Thumb>() is not null);
}
