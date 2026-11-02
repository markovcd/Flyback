using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Flyback.Editor.Windows;

/// <summary>
/// Fades a control out once it has been left alone for a while, and brings it back at
/// the next key, or move or press of the pointer: the patch over a live picture.
/// </summary>
/// <param name="busy">Whether something is under way that the fade must wait for, such as a drag.</param>
internal sealed class IdleFade(Func<bool> busy)
{
    private static readonly TimeSpan Fading = TimeSpan.FromMilliseconds(400);

    private DispatcherTimer? timer;
    private Control? faded;
    private Transitions? transitionsBefore;
    private bool wakesOnPointer;

    /// <summary>The pointer whose press woke the control, which does nothing else until it lets go.</summary>
    private IPointer? waking;

    /// <summary>Whether the taps of the press that woke the control are still to be swallowed.</summary>
    private bool tapsSpent;

    /// <summary>Whether the control is faded out, or fading.</summary>
    public bool IsFaded { get; private set; }

    /// <summary>
    /// Starts watching <paramref name="control"/>, shown, to fade it once left alone for
    /// <paramref name="wait"/>; a wait of zero never fades it.
    /// </summary>
    /// <param name="pointer">Whether the pointer brings it back as well as the keys.</param>
    public void Start(Control control, TimeSpan wait, bool pointer = true)
    {
        Stop();

        if (wait <= TimeSpan.Zero) return;

        faded = control;
        wakesOnPointer = pointer;
        transitionsBefore = control.Transitions;
        control.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = Fading }];

        // A press wakes the control from here, where it is first asked whether it was faded.
        foreach (var swallowed in Swallowed) control.AddHandler(swallowed, Swallow, RoutingStrategies.Tunnel, handledEventsToo: true);

        if (pointer) control.AddHandler(InputElement.PointerMovedEvent, Woken, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        control.AddHandler(InputElement.KeyDownEvent, Woken, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        timer = new DispatcherTimer { Interval = wait };
        timer.Tick += (_, _) => Fade();

        Wake();
    }

    /// <summary>Stops watching, and leaves the control shown as it was found.</summary>
    public void Stop()
    {
        timer?.Stop();
        timer = null;

        if (faded is not { } control) return;

        control.RemoveHandler(InputElement.PointerMovedEvent, Woken);
        control.RemoveHandler(InputElement.KeyDownEvent, Woken);
        foreach (var swallowed in Swallowed) control.RemoveHandler(swallowed, Swallow);
        control.Transitions = transitionsBefore;
        control.Opacity = 1;
        IsFaded = false;
        waking = null;
        tapsSpent = false;

        faded = null;
        transitionsBefore = null;
    }

    /// <summary>Shows the control and starts the wait again.</summary>
    public void Wake()
    {
        if (faded is null || timer is null) return;

        faded.Opacity = 1;
        IsFaded = false;
        timer.Stop();
        timer.Start();
    }

    /// <summary>Fades the control now, unless something is under way.</summary>
    public void Fade()
    {
        if (faded is null || timer is null) return;

        timer.Stop();

        if (busy())
        {
            timer.Start();
            return;
        }

        faded.Opacity = 0;
        IsFaded = true;
    }

    private static readonly RoutedEvent[] Swallowed =
    [
        InputElement.PointerPressedEvent, InputElement.PointerReleasedEvent, InputElement.PointerMovedEvent,
        InputElement.PointerWheelChangedEvent, InputElement.TappedEvent, InputElement.DoubleTappedEvent,
    ];

    private void Woken(object? sender, RoutedEventArgs e) => Wake();

    /// <summary>
    /// A pointer that lands on the faded control goes no further: nothing unseen is selected,
    /// dragged or turned, and the picture under it is not clicked. A press that may wake it
    /// does, and is spent on that, taps and all.
    /// </summary>
    private void Swallow(object? sender, RoutedEventArgs e)
    {
        var routed = e.RoutedEvent;
        var pointer = (e as PointerEventArgs)?.Pointer;

        if (routed == InputElement.PointerPressedEvent && pointer != waking)
        {
            waking = null;
            tapsSpent = false;
        }

        if (waking is not null && pointer == waking)
        {
            e.Handled = true;

            if (routed != InputElement.PointerReleasedEvent) return;

            waking = null;
            tapsSpent = true;
            return;
        }

        if (tapsSpent && (routed == InputElement.TappedEvent || routed == InputElement.DoubleTappedEvent))
        {
            e.Handled = true;
            return;
        }

        var pressed = wakesOnPointer && routed == InputElement.PointerPressedEvent;

        if (!IsFaded)
        {
            if (pressed) Wake();
            return;
        }

        e.Handled = true;

        if (!pressed) return;

        waking = pointer;
        Wake();
    }
}
