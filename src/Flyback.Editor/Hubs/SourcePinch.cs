using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Flyback.Editor.Canvas;

namespace Flyback.Editor.Hubs;

/// <summary>
/// A pinch over the text, by two fingers on a screen or on a trackpad, resizes its font
/// in whole points rather than zooming the view, as a phone or a code editor does.
/// </summary>
/// <remarks>
/// The fingers are followed on the way down, ahead of the text area, which takes a
/// press for the caret before a gesture recognizer on the editor would see it.
/// </remarks>
internal sealed class SourcePinch
{
    private readonly TextEditor editor;
    private readonly Func<double> size;
    private readonly Action<double> resize;

    /// <summary>Where each finger on the text is, in the editor's coordinates.</summary>
    private readonly Dictionary<int, Point> fingers = [];

    /// <summary>How far apart the two fingers were and the size then, while a pinch is under way.</summary>
    private (double Apart, double Size)? pinch;

    /// <summary>The trackpad's running size, kept between its steps so small ones add up.</summary>
    private double magnified;

    /// <param name="editor">The text whose font a pinch resizes.</param>
    /// <param name="size">Reads the font size.</param>
    /// <param name="resize">Sets the font size, which clamps it to the setting's bounds.</param>
    public SourcePinch(TextEditor editor, Func<double> size, Action<double> resize)
    {
        this.editor = editor;
        this.size = size;
        this.resize = resize;
        magnified = size();

        editor.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Lifted(e.Pointer), RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerCaptureLostEvent, (_, e) => Lifted(e.Pointer));
        editor.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, Magnified);
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch) return;

        fingers[e.Pointer.Id] = e.GetPosition(editor);

        if (fingers.Count != 2) return;

        // The second finger starts a pinch rather than a selection.
        e.Handled = true;
        pinch = (Apart(), size());
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (!fingers.ContainsKey(e.Pointer.Id)) return;

        fingers[e.Pointer.Id] = e.GetPosition(editor);

        if (pinch is not { } from) return;

        // Neither finger scrolls or selects while the two pinch.
        e.Handled = true;

        if (from.Apart > 0) resize(Math.Round(from.Size * Apart() / from.Apart));
    }

    private void Lifted(IPointer pointer)
    {
        fingers.Remove(pointer.Id);

        if (fingers.Count < 2) pinch = null;
    }

    /// <summary>How far apart the first two fingers are.</summary>
    private double Apart()
    {
        var both = fingers.Values.Take(2).ToArray();
        return Point.Distance(both[0], both[1]);
    }

    /// <summary>A trackpad pinch, told as the change since its last step.</summary>
    private void Magnified(object? sender, PointerDeltaEventArgs e)
    {
        e.Handled = true;

        // A magnification of -1 or less would be a size of nothing.
        if (e.Delta.X <= -0.9) return;

        // Ctrl+scroll or a button moved the size since, so start again from where it stands.
        if (Math.Round(magnified) != size()) magnified = size();

        magnified = Math.Clamp(magnified * (1 + e.Delta.X), CanvasSettings.MinEditorFontSize, CanvasSettings.MaxEditorFontSize);
        resize(Math.Round(magnified));
    }
}
