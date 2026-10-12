using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Flyback.Ui.Controls;

/// <summary>
/// A finger held still on anything with a tooltip opens the tip, since a finger never
/// hovers; held on a button, the lift that follows does not press it.
/// </summary>
/// <remarks>
/// The tip stays up until the next press anywhere. A repeat button is left alone, because
/// holding it is how it is used.
/// </remarks>
internal sealed class TouchTips
{
    /// <summary>How long a finger stays still before it is held rather than tapping.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(450);

    private readonly DispatcherTimer holdTimer;

    private IPointer? finger;
    private Point from;
    private Control? waiting;

    public TouchTips()
    {
        holdTimer = new DispatcherTimer { Interval = HoldTime };
        holdTimer.Tick += (_, _) => Held();
    }

    /// <summary>The control whose tip a held finger opened, null while none is up.</summary>
    public Control? Open { get; private set; }

    /// <summary>Answers every finger on <paramref name="top"/>, whatever it lands on.</summary>
    public void Attach(TopLevel top)
    {
        top.AddHandler(InputElement.PointerPressedEvent, (_, e) => Pressed(top, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerMovedEvent, (_, e) => Moved(top, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Released(e), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The finger has been still for <see cref="HoldTime"/>: its tip goes up.</summary>
    public void Held()
    {
        holdTimer.Stop();

        if (waiting is null || finger is null) return;

        // A scroll that took the finger over sends no moves here, only takes the capture.
        if (finger.Captured is not Visual captured || !(captured == waiting || waiting.IsVisualAncestorOf(captured) || captured.IsVisualAncestorOf(waiting)))
        {
            Cancel();
            return;
        }

        Open = waiting;
        waiting = null;
        ToolTip.SetIsOpen(Open, true);

        if (captured.FindAncestorOfType<Button>(includeSelf: true) is not { } button) return;

        // A finger is captured by whatever it landed on, often the glyph inside the button,
        // and only the button's own lost capture lets go of its press.
        finger.Capture(button);
        finger.Capture(null);
    }

    private void Pressed(TopLevel top, PointerPressedEventArgs e)
    {
        Close();
        Cancel();

        if (e.Pointer.Type != PointerType.Touch) return;

        var at = e.GetPosition(top);

        if (TipAt(top, at, e.Source as Visual) is not { } holder) return;

        finger = e.Pointer;
        from = at;
        waiting = holder;
        holdTimer.Start();
    }

    private void Moved(TopLevel top, PointerEventArgs e)
    {
        if (e.Pointer != finger || waiting is null) return;

        if (Point.Distance(from, e.GetPosition(top)) >= FingerSwipe.Slop) Cancel();
    }

    private void Released(PointerReleasedEventArgs e)
    {
        if (e.Pointer != finger) return;

        Cancel();
    }

    private void Cancel()
    {
        holdTimer.Stop();
        finger = null;
        waiting = null;
    }

    private void Close()
    {
        if (Open is null) return;

        ToolTip.SetIsOpen(Open, false);
        Open = null;
    }

    /// <summary>
    /// The nearest control under <paramref name="at"/> with a tip, found as the tooltip
    /// service finds one, disabled controls included since a finger lands on them too.
    /// </summary>
    private static Control? TipAt(TopLevel top, Point at, Visual? source)
    {
        var visual = top.GetVisualAt(at, v => v.IsVisible && v is not InputElement { IsHitTestVisible: false }) ?? source;

        for (; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is not Control control) continue;
            if (control is RepeatButton || !ToolTip.GetServiceEnabled(control)) return null;

            if (ToolTip.GetTip(control) is not null && (control.IsEffectivelyEnabled || ToolTip.GetShowOnDisabled(control)))
                return control;
        }

        return null;
    }
}
