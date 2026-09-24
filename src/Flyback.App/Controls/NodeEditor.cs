using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// The canvas: one control that draws the patch and hands the pointer and the keys to
/// the services around it (ADR-0017, ADR-0150).
/// </summary>
/// <remarks>
/// Everything is drawn rather than built from controls, which keeps panning and
/// zooming over a few hundred modules cheap. What the canvas holds and does is in its
/// services, which the container builds together: the patch and its history, the
/// selection, the view, the edits, the gestures and the painting. What is left here is
/// what only a control can be: its size, its keys, and where its pointer goes.
/// </remarks>
internal sealed class NodeEditor : Control
{
    private readonly CanvasPainter painter;
    private readonly CanvasReport report;

    public NodeEditor(
        CanvasHistory history,
        CanvasSelection selection,
        Viewport view,
        CanvasEdits edits,
        CanvasClipboard clipboard,
        CanvasGestures gestures,
        CanvasTips tips,
        SocketDial dial,
        KnobLinking linking,
        UndescribedTags tags,
        CanvasPainter painter,
        Repaint repaint,
        CanvasReport report,
        NodeGeometry geometry)
    {
        History = history;
        Geometry = geometry;
        Selection = selection;
        View = view;
        Edits = edits;
        Clipboard = clipboard;
        Gestures = gestures;
        Tips = tips;
        Dial = dial;
        Linking = linking;
        Tags = tags;

        this.painter = painter;
        this.report = report;

        Focusable = true;
        ClipToBounds = true;

        repaint.Requested += InvalidateVisual;
        history.PatchChanged += (_, _) => InvalidateVisual();
    }

    internal CanvasHistory History { get; }

    internal CanvasSelection Selection { get; }

    internal Viewport View { get; }

    internal CanvasEdits Edits { get; }

    internal CanvasClipboard Clipboard { get; }

    internal CanvasGestures Gestures { get; }

    internal CanvasTips Tips { get; }

    internal SocketDial Dial { get; }

    internal KnobLinking Linking { get; }

    internal UndescribedTags Tags { get; }

    /// <summary>Where each part of a module sits, drawn compact or in full.</summary>
    internal NodeGeometry Geometry { get; }

    /// <summary>What the canvas has to say, which the window puts on its report line.</summary>
    internal CanvasReport Report => report;

    /// <summary>The graph-to-control matrix, for a test asking where a socket ended up on the control.</summary>
    internal Matrix GraphToScreen => View.GraphToScreen;

    public override void Render(DrawingContext context) => painter.Render(context);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        View.Resize(e.NewSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        Gestures.Pressed(this, e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Gestures.Moved(this, e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        Gestures.Released(this, e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        Gestures.CaptureLost(this);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Gestures.Wheel(this, e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        Tips.Down(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Backing out of the gesture under way, as Escape does everywhere else here.
        // Before the modifier check, because the hand may still hold the Ctrl that
        // began it; unhandled with nothing under way, so the window still gets it.
        if (e.Key == Key.Escape && (Gestures.Abort(this) || Selection.EndPeek()))
        {
            e.Handled = true;
            return;
        }

        var editable = Gestures.Editable;

        // Copy and paste here rather than on the window: Ctrl+C in a text box means the
        // text in it, and the canvas only sees these while it has the focus.
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            // Copy, select-all and framing only look, so a locked canvas keeps them.
            Action? command = e.Key switch
            {
                Key.C => () => UseClipboard(Clipboard.CopyAsync),
                Key.X when editable => () => UseClipboard(Clipboard.CutAsync),
                Key.V when editable => () => UseClipboard(Clipboard.PasteAsync),

                // Duplicate leaves the clipboard alone, so what was copied earlier survives.
                Key.D when editable => Edits.DuplicateSelection,
                Key.A => Selection.SelectAll,

                // Shift tells group from ungroup, and shut from open, the pairing undo and redo use.
                Key.G when editable => shift ? Edits.UngroupSelected : Edits.GroupSelected,
                Key.E when editable => shift ? Edits.CloseSelectedGroups : Edits.OpenSelectedGroups,

                // Bypass, on the letter a desk uses for it; a module is off or it is on.
                Key.B when editable => Edits.SwitchSelected,

                // Under Control, since every bare letter belongs to the instrument.
                Key.F => View.FrameAll,
                _ => null,
            };

            // Anything else with a modifier on it is the window's: undo and redo.
            if (command is null) return;

            command();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Delete or Key.Back when editable:
                Edits.DeleteSelected();
                e.Handled = true;
                break;

            // The palette, from the keyboard, where the pointer last was.
            case Key.Space when editable:
                Gestures.RequestMenu();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Runs one of the clipboard gestures against this window's clipboard and says
    /// whatever it has to say.
    /// </summary>
    /// <remarks>
    /// Void and asynchronous, which is what a key press is: an exception escaping would
    /// have no caller to reach and would take the program with it.
    /// </remarks>
    private async void UseClipboard(Func<Avalonia.Input.Platform.IClipboard?, Task<string?>> gesture)
    {
        try
        {
            if (await gesture(TopLevel.GetTopLevel(this)?.Clipboard) is { } trouble) report.Say(trouble);
        }
        catch (Exception ex)
        {
            report.Say($"Clipboard unavailable: {ex.Message}");
        }
    }
}
