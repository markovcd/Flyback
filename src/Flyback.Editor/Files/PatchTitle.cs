using Flyback.Core;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;

namespace Flyback.Editor.Files;

/// <summary>Keeps the window title in step with the patch: its name, and a dot while closing would lose something.</summary>
internal sealed class PatchTitle(UnsavedWork unsaved, PatchFiles files, ITitle title)
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

    private void Refresh()
    {
        var named = files.Name is null ? BaseTitle : $"{files.Name} — {BaseTitle}";
        title.Set(unsaved.SomethingToLose ? named + " •" : named);
    }
}
