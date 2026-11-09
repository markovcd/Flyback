using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;

namespace Flyback.Editor.Assist;

internal sealed class AssistantConversation : IPatchConversation
{
    private readonly Func<Patch> current;
    private SavedConversation? settled;
    private PatchShape? anchored;
    private bool unsaved;

    public AssistantConversation(NodeEditor editor) : this(() => editor.History.Patch)
    {
    }

    internal AssistantConversation(Func<Patch> current)
    {
        this.current = current;
    }

    public SavedConversation? Waiting { get; private set; }

    public bool ConversationUnsaved => unsaved && Belongs();

    /// <summary>Whether a turn is in flight, which nothing can save: closing the patch ends it.</summary>
    public bool Working { get; set; }

    public event EventHandler? Opened;
    public event EventHandler? Saved;

    public void Open(string? saved)
    {
        Waiting = SavedConversation.Read(saved);
        anchored = Waiting is null ? null : PatchShape.Of(current());
        settled = Waiting;
        unsaved = false;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Waiting = null;
        settled = null;
        anchored = null;
        unsaved = false;
    }

    public void Begin(SavedConversation? resuming, Patch patch)
    {
        Waiting = null;
        anchored = PatchShape.Of(patch);

        if (resuming is null) settled = null;
    }

    public void Rebase(Patch patch) => anchored = PatchShape.Of(patch);

    public bool WaitingMoved(Patch patch) => Moved(anchored, patch);

    public void Settle(SavedConversation conversation)
    {
        settled = conversation;
        unsaved = true;
    }

    public string? ConversationToSave() => Belongs() ? settled!.ToJson() : null;

    public void ConversationSaved()
    {
        unsaved = false;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private bool Belongs() =>
        settled is not null && !Moved(anchored, current());

    private static bool Moved(PatchShape? on, Patch now) => on is null || !on.Matches(now);
}
