using Flyback.Assist;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Assist;

/// <summary>
/// Creates one assistant conversation from the current editor, catalog, and settings.
/// </summary>
internal sealed class AssistantRunFactory(
    PluginCatalog plugins,
    IAssistantEditor editor,
    AssistantSettingRepository settings)
{
    public AssistantRun Create(IPatchAssistant assistant, AssistantConfig config, SavedConversation? resuming = null) =>
        new(
            assistant,
            config,
            plugins.Modules,
            editor.Current,
            settings.Current.TurnLimit,
            samples: editor.Samples,
            pictures: editor.Pictures,
            resuming: resuming,
            prose: settings.GetProsePolicy(),
            presets: editor.Presets() ?? plugins.Presets);
}
