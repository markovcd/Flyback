using Avalonia.Controls;
using Flyback.Core;
using Flyback.Editor.Bars;
using Flyback.Editor.Files;
using Flyback.Editor.Inspect;
using Flyback.Editor.Notices;

namespace Flyback.Editor;

/// <summary>
/// Keeps the toolbar buttons, the window title, and the inspector panel in step
/// with the document's edit state and ownership (ADR-0150).
/// </summary>
internal sealed class EditState(
    Document document,
    UnsavedWork unsaved,
    PatchFiles files,
    Toolbar toolbar,
    Inspector inspector,
    ITitle title)
    : IReactTo<HistoryChanged>,
        IReactTo<EditStateChanged>,
        IReactTo<ConversationChanged>,
        IReactTo<OwnershipChanged>
{
    private const string BaseTitle = GlobalConstants.ApplicationName;

    public Task On(HistoryChanged notice)
    {
        Refresh();
        return Task.CompletedTask;
    }

    public Task On(EditStateChanged notice)
    {
        Refresh();
        return Task.CompletedTask;
    }

    public Task On(ConversationChanged notice)
    {
        Refresh();
        return Task.CompletedTask;
    }

    public Task On(OwnershipChanged notice)
    {
        RefreshOwnership();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Grays Undo and Redo out when there is nothing behind or ahead, and puts the
    /// patch name and the unsaved dot in the window title.
    /// </summary>
    public void Refresh()
    {
        toolbar.Undo.IsEnabled = document.CanUndo;
        toolbar.Redo.IsEnabled = document.CanRedo;

        var named = files.Name is null ? BaseTitle : $"{files.Name} — {BaseTitle}";
        title.Set(unsaved.SomethingToLose ? named + " •" : named);
    }

    /// <summary>
    /// Puts the tidy button, the inspector panel and the edit state in step with
    /// who owns the patch and which view is showing.
    /// </summary>
    public void RefreshOwnership()
    {
        toolbar.Tidy.IsEnabled = document.ShowingCode || !document.Owned;

        ToolTip.SetTip(toolbar.Tidy, document.ShowingCode
            ? "Fold the long lines so the patch reads down the page  (Ctrl+L)"
            : document.Owned
                ? "The text is the document, so the canvas is laid out from it on every "
                  + "apply. Fold the text instead."
                : Toolbar.TidyTip);

        inspector.Build();
        Refresh();

        ToolTip.SetTip(
            inspector.Panel,
            document.Owned
                ? "The text is the document. A knob turned here is written back into it "
                  + "where it already says it."
                : null);
    }
}
