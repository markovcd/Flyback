namespace Flyback.Core.Graph;

/// <summary>A bundle read back: the patch, the files it names, and the conversation saved with it.</summary>
/// <param name="Files">
/// Keyed by the path the patch stores, which is the path this wrote into it when
/// it was packed — so a library serving these needs no rules about folders.
/// </param>
/// <param name="Conversation">
/// The text of <see cref="PatchBundle.ConversationEntry"/>, or null for a bundle
/// saved with none.
/// </param>
/// <param name="Load">
/// How the patch inside read, which is what says whether it is all there: a
/// bundle from a later version, or one naming a module this build does not have,
/// reads without throwing and is not the patch that was packed. Null only for a
/// value made by hand.
/// </param>
public readonly record struct LoadedBundle(
    Patch Patch,
    IReadOnlyDictionary<string, byte[]> Files,
    string? Conversation = null,
    PatchLoad? Load = null);