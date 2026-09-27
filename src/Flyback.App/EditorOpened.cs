using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Updates;

namespace Flyback.App;

/// <summary>Runs the editor's launch sequence once its window can show dialogs and resolve files.</summary>
internal sealed class EditorOpened(
    EditorSetup setup,
    IDialog dialog,
    WorkKeeper keeper,
    WorkRecovery recovery,
    PatchOpening opening,
    PresetSlot presets)
{
    public async Task RunAsync()
    {
        if (setup.WhatsNew is not null)
            await dialog.Show(WhatsNew.Title(setup.WhatsNew), WhatsNew.View(setup.WhatsNew));

        keeper.Restore(recovery.Restore);

        if (setup.OpenPath is { } path) await opening.OpenPathAsync(path);

        if (setup.OpenShared is { Length: > 0 } id) await presets.OpenSharedAgainAsync(id);
    }
}
