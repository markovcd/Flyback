using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// What a crash would lose, and putting it back at the next start — ADR-0103.
/// </summary>
/// <remarks>
/// What is handed to <see cref="WorkKeeper"/> is the document as the unsaved question
/// sees it — the patch, the text where the text is the document, what a bundle carried
/// and the conversation — so that what comes back is what that question would have
/// offered to save.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>Unsaved work kept against a crash, or null where none is kept — which is every test.</summary>
    private readonly WorkKeeper? keeper;

    /// <summary>The document as a crash would lose it, or null while there is nothing to lose.</summary>
    private RecoveredWork? Work() => !SomethingToLose ? null : new(
        files.Name,
        files.SoundFolder.Beside,
        PatchIO.ToJson(editor.Patch),
        document.Owned ? document.Text : null,
        assistant?.ConversationToSave(),
        files.Carried?.Bytes);

    /// <summary>
    /// Puts work a crash left behind back on the canvas, as the document it was and
    /// unsaved — it is on no disk anybody chose.
    /// </summary>
    /// <returns>
    /// Whether it came back. A patch naming a module no plugin now offers is refused
    /// as a file would be, and kept for a start that has the plugin again.
    /// </returns>
    internal bool Recover(RecoveredWork work)
    {
        var loaded = PatchIO.Read(work.Patch);

        if (!loaded.IsComplete)
        {
            Report($"Not restored. {loaded.Summary}", loaded.Detail);
            return false;
        }

        files.Became(
            work.Name,
            work.Beside,
            work.Files is { } held ? new BundleFiles(held, files.SoundFolder, files.PictureFolder) : null);

        ClearPresetSelection();

        editor.Patch = loaded.Patch;
        RewindToZero();

        if (work.Source is { } text)
        {
            // Written nowhere, so all of it is unsaved text.
            document.TakeSource(text, saved: false);
        }
        else
        {
            document.DropSource();
        }

        assistant?.Open(work.Conversation);
        editor.MarkUnsaved();

        // Kept at once, since the orphan it came from is about to go.
        keeper?.Keep(later: false);

        Report($"Restored {work.Name ?? "the patch"} after a crash. It has not been saved.");

        return true;
    }
}
