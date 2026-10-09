using Flyback.Core;
using Flyback.Editor.Bars;
using Flyback.Editor.Files;
using Flyback.Editor.Notices;

namespace Flyback.Editor;

/// <summary>
/// Keeps Undo, Redo and the window title in step with the document's edit state
/// (ADR-0150).
/// </summary>
internal sealed class EditState(
    Document document,
    UnsavedWork unsaved,
    PatchFiles files,
    Toolbar toolbar,
    ITitle title)
    : IReactTo<HistoryChanged>,
        IReactTo<EditStateChanged>,
        IReactTo<ConversationChanged>,
        IReactTo<OwnershipChanged>,
        IStartAt
{
    /// <summary>Before the first patch arrives, so the title is never the window's default.</summary>
    public StartPhase Phase => StartPhase.Shown;

    public Task On()
    {
        Refresh();
        return Task.CompletedTask;
    }

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
        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Grays Undo and Redo out when there is nothing behind or ahead, and puts the
    /// patch name and the unsaved dot in the window title.
    /// </summary>
    private void Refresh()
    {
        toolbar.Undo.IsEnabled = document.CanUndo;
        toolbar.Redo.IsEnabled = document.CanRedo;

        var named = files.Name is null ? BaseTitle : $"{files.Name} — {BaseTitle}";
        title.Set(unsaved.SomethingToLose ? named + " •" : named);
    }
}
