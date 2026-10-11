namespace Flyback.Editor.Hubs;

/// <summary>The conversation about the patch, which its file carries and gives back (ADR-0072); the assistant fulfils it.</summary>
internal interface IPatchConversation
{
    /// <summary>The conversation as the file keeps it, or null where there is none worth keeping.</summary>
    string? ConversationToSave();

    /// <summary>What <see cref="ConversationToSave"/> gave is on disk now.</summary>
    void ConversationSaved();

    /// <summary>Takes up the conversation a file was opened with, or none.</summary>
    void Open(string? saved);
}
