using Flyback.App.Canvas;
using Flyback.Core.Graph;

namespace Flyback.App.Assist;

internal sealed class AssistantConversation
{
    private readonly Func<Patch> current;
    private SavedConversation? settled;
    private (Patch Patch, int Nodes, int Wires)? anchored;
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

    public event EventHandler? Opened;
    public event EventHandler? Saved;

    public void Open(string? saved)
    {
        Waiting = SavedConversation.Read(saved);
        anchored = Waiting is null ? null : Anchor(current());
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

    public void Begin(SavedConversation? resuming, Patch current)
    {
        Waiting = null;
        anchored = Anchor(current);

        if (resuming is null) settled = null;
    }

    public void Rebase(Patch current) => anchored = Anchor(current);

    public bool WaitingMoved(Patch current) => Moved(anchored, current);

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

    private static (Patch Patch, int Nodes, int Wires) Anchor(Patch patch) =>
        (patch, patch.Nodes.Count, patch.Connections.Count);

    private static bool Moved((Patch Patch, int Nodes, int Wires)? on, Patch now) =>
        on is not { } was
        || !ReferenceEquals(was.Patch, now)
        || was.Nodes != now.Nodes.Count
        || was.Wires != now.Connections.Count;
}
