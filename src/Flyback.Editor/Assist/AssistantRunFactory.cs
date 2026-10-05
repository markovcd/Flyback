using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Assist;

/// <summary>
/// Creates one assistant conversation from the current editor, catalog, and settings.
/// </summary>
internal sealed class AssistantRunFactory(
    PluginCatalog plugins,
    IAssistantEditor editor,
    AssistantSettingRepository settings)
{
    /// <param name="over">The patch to work on, in place of the one on the canvas.</param>
    public AssistantRun Create(IPatchAssistant assistant, AssistantConfig config, SavedConversation? resuming = null, Patch? over = null) =>
        new(
            assistant,
            config,
            plugins.Modules,
            over ?? editor.Current,
            settings.Current.TurnLimit,
            samples: editor.Samples,
            pictures: editor.Pictures,
            resuming: resuming,
            prose: settings.GetProsePolicy(),
            presets: editor.Presets() ?? plugins.Presets);
}
