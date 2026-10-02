using Flyback.Editor.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Updates;

namespace Flyback.Editor;

/// <summary>Runs the editor's launch sequence once its window can show dialogs and resolve files.</summary>
internal sealed class EditorOpened(
    EditorLaunch launch,
    IDialog dialog,
    WorkKeeper keeper,
    WorkRecovery recovery,
    PatchOpening opening,
    PresetSlot presets)
{
    public async Task RunAsync()
    {
        if (launch.WhatsNew is not null)
            await dialog.Show(WhatsNew.Title(launch.WhatsNew), WhatsNew.View(launch.WhatsNew));

        keeper.Restore(recovery.Restore);

        if (launch.OpenPath is { } path) await opening.OpenPathAsync(path);

        if (launch.OpenShared is { Length: > 0 } id) await presets.OpenSharedAgainAsync(id);
    }
}
