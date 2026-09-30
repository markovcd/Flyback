using Avalonia.Input;
using Avalonia.Interactivity;

namespace Flyback.App;

/// <summary>
/// Whether the last press in the editor was a finger, so what opens under one leaves its
/// text box to be tapped rather than throwing up the on-screen keyboard.
/// </summary>
internal sealed class LastPress
{
    public bool ByFinger { get; private set; }

    /// <summary>Follows every press and key inside <paramref name="root"/>, handled or not.</summary>
    public void Watch(InputElement root)
    {
        root.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => ByFinger = e.Pointer.Type == PointerType.Touch,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        root.AddHandler(InputElement.KeyDownEvent, (_, _) => ByFinger = false, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>A finger on the canvas, however it was handed to <see cref="Canvas.Fingers"/>.</summary>
    public void Finger() => ByFinger = true;
}
