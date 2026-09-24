using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Flyback.App;

/// <summary>
/// What the window shows of the <see cref="Document"/>: the code button, the tidy
/// button, and the panel's gestures that end in a write-back.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Called once, as the window is built.</summary>
    private void WireDocument()
    {
        document.EditStateChanged += (_, _) => RefreshEditState();
        document.PanelStale += (_, _) => inspector.Build();
        document.OwnershipChanged += (_, _) => ShowOwnership();
        document.ViewChanged += (_, _) =>
        {
            if (toolbar.Code.IsChecked != document.ShowingCode) toolbar.Code.IsChecked = document.ShowingCode;
        };

        toolbar.Code.IsCheckedChanged += (_, _) => document.ShowCode(toolbar.Code.IsChecked == true);

        source.EditorFontSize = canvasSection.EditorFontSize;
        source.EditorFontSizeChanged += (_, size) => canvasSection.SaveEditorFontSize(size);

        // The buffer is emptied by the handover and written nowhere on the way, so
        // typing not on disk yet is asked about as it is when a document is closed over.
        source.HandBackRequested += async (_, _) =>
        {
            if (document.Owned && await MayLoseTheTextAsync()) document.HandBack();
        };

        // Caught on the way up and after whoever handled it, because a slider
        // captures the pointer: letting go halfway across the window is still
        // letting go of the slider, and the value written should be the one the
        // control finished on.
        inspector.Panel.AddHandler(
            PointerReleasedEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // A number typed rather than dragged has no gesture to wait for, and the
        // focus going is the surest end of one.
        inspector.Panel.AddHandler(LostFocusEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble);

        // And a key let go of, because a number box takes what is typed as it is
        // typed. On the way up rather than down, because the character is taken
        // between the two; any key, since an arrow steps the value and a backspace
        // clears it without giving up the focus.
        inspector.Panel.AddHandler(
            KeyUpEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // And a notch of the wheel, the one way a number box moves that touches
        // neither the pointer's button nor the focus. Each notch is finished the
        // moment it lands.
        inspector.Panel.AddHandler(
            PointerWheelChangedEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>
    /// Puts the tidy button, the panel and the edit state in step with who owns the
    /// patch and which view is showing.
    /// </summary>
    private void ShowOwnership()
    {
        // Laying out is off only where it would not last: a locked canvas is
        // re-laid on the next evaluation, so tidying one is work thrown away.
        // Showing the text, the same button folds the lines instead.
        toolbar.Tidy.IsEnabled = document.ShowingCode || !document.Owned;

        ToolTip.SetTip(toolbar.Tidy, document.ShowingCode
            ? "Fold the long lines so the patch reads down the page  (Ctrl+L)"
            : document.Owned
                ? "The text is the document, so the canvas is laid out from it on every "
                  + "apply. Fold the text instead."
                : Toolbar.TidyTip);

        // What the empty panel says is a list of gestures, and half of them
        // have just been switched off or back on.
        inspector.Build();
        RefreshEditState();

        ToolTip.SetTip(
            inspector.Panel,
            document.Owned
                ? "The text is the document. A knob turned here is written back into it "
                  + "where it already says it."
                : null);
    }
}
