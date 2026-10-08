using Avalonia.Input;
using Avalonia.Interactivity;

namespace Flyback.Editor;

/// <summary>
/// Whether the last press in the editor was a finger, so what opens under one leaves its
/// text box to be tapped rather than throwing up the on-screen keyboard.
/// </summary>
internal sealed class LastPress
{
    public bool ByFinger
    {
        get;
        private set
        {
            if (field == value) return;

            field = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The hand changed: a finger after a mouse or a key, or the other way round.</summary>
    public event Action? Changed;

    /// <summary>Follows every press and key inside <paramref name="root"/>, handled or not.</summary>
    public void Watch(InputElement root)
    {
        root.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => ByFinger = onlyFingers || e.Pointer.Type == PointerType.Touch,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        root.AddHandler(InputElement.KeyDownEvent, (_, _) => ByFinger = onlyFingers, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Every press is a finger's from now on, keys included, as on a phone, whose keys are its on-screen keyboard's.</summary>
    public void OnlyFingers()
    {
        onlyFingers = true;
        ByFinger = true;
    }

    private bool onlyFingers;

    /// <summary>A finger on the canvas, however it was handed to <see cref="Canvas.Fingers"/>.</summary>
    public void Finger() => ByFinger = true;
}
