using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Flyback.Editor.Windows;

/// <summary>
/// Fades a control out once the pointer and the keys have left it alone for a while, and
/// brings it back at the next move or press: the modules over a live picture.
/// </summary>
/// <param name="busy">Whether something is under way that the fade must wait for, such as a drag.</param>
internal sealed class ModulesFade(Func<bool> busy)
{
    /// <summary>How long the control is left alone before it fades.</summary>
    internal static readonly TimeSpan Idle = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Fading = TimeSpan.FromMilliseconds(400);

    private DispatcherTimer? timer;
    private Control? faded;
    private Transitions? transitionsBefore;

    /// <summary>Whether the control is faded out, or fading.</summary>
    public bool IsFaded { get; private set; }

    /// <summary>Starts watching <paramref name="control"/>, shown.</summary>
    public void Start(Control control)
    {
        Stop();

        faded = control;
        transitionsBefore = control.Transitions;
        control.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = Fading }];
        control.AddHandler(InputElement.PointerMovedEvent, Woken, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(InputElement.PointerPressedEvent, Woken, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(InputElement.KeyDownEvent, Woken, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        timer = new DispatcherTimer { Interval = Idle };
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
        control.RemoveHandler(InputElement.PointerPressedEvent, Woken);
        control.RemoveHandler(InputElement.KeyDownEvent, Woken);
        control.Transitions = transitionsBefore;
        control.Opacity = 1;
        IsFaded = false;

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

    private void Woken(object? sender, RoutedEventArgs e) => Wake();
}
