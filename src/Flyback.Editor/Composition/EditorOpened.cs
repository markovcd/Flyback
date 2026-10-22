using Flyback.Editor.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Editor.Updates;

namespace Flyback.Editor.Composition;

/// <summary>Runs the editor's launch sequence once its window can show dialogs and resolve files.</summary>
internal sealed class EditorOpened(
    EditorLaunch launch,
    IDialog dialog,
    WorkKeeper keeper,
    WorkRecovery recovery,
    PatchOpening opening,
    PresetSlot presets,
    UsageQuestion usageQuestion) : IStartAt
{
    public StartPhase Phase => StartPhase.Opened;

    public async Task On()
    {
        if (launch.WhatsNew is not null)
            await dialog.Show(WhatsNew.Title(launch.WhatsNew), WhatsNew.View(launch.WhatsNew));

        await usageQuestion.Ask();

        keeper.Restore(recovery.Restore);

        if (launch.OpenPath is { } path) await opening.OpenPathAsync(path);

        if (launch.OpenShared is { Length: > 0 } id) await presets.OpenSharedAgainAsync(id);
    }
}
