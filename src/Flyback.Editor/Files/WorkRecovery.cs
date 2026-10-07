using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;

namespace Flyback.Editor.Files;

/// <summary>Restores the document a crash left behind into this editor run.</summary>
internal sealed class WorkRecovery(
    PatchFiles files,
    Playback playback,
    Document document,
    AssistantPanel assistant,
    NodeEditor editor,
    WorkKeeper keeper,
    ReportLine report)
{
    /// <summary>Restores complete work and keeps it until this run saves or closes.</summary>
    public bool Restore(RecoveredWork work)
    {
        var loaded = PatchIO.Read(work.Patch);

        if (!loaded.IsComplete)
        {
            report.Say($"Not restored. {loaded.Summary}", loaded.Detail);
            return false;
        }

        files.Became(
            work.Name,
            work.Beside,
            work.Files is { } held ? new BundleFiles(held, files.SoundFolder, files.PictureFolder) : null);

        playback.Show(loaded.Patch);

        if (work.Source is { } text) document.TakeSource(text, saved: false);
        else document.DropSource();

        assistant.Open(work.Conversation);
        editor.History.MarkUnsaved();

        // Keep the recovered document at once before its orphan is removed.
        keeper.Keep(later: false);

        report.Say($"Restored {work.Name ?? "the patch"} after a crash. It has not been saved.");

        return true;
    }
}
