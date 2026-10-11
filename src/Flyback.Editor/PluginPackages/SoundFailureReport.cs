using Flyback.Editor.Assist;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.PluginPackages;

/// <summary>Says a sound failure with the run's plugins as its detail: what loaded, what did not, and which assistant is chosen.</summary>
internal sealed class SoundFailureReport(PluginCatalog plugins, Playback playback, ChosenAssistant chosenAssistant, ReportLine report)
    : IReactTo<SoundFailed>
{
    public Task On(SoundFailed notice)
    {
        report.Say(notice.Message, PluginSummary.Text(plugins, playback.Sound.Failure, chosenAssistant.Summary));
        return Task.CompletedTask;
    }
}
