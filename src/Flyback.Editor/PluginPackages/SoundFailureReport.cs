using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.PluginPackages;

/// <summary>Says a sound failure with the run's plugins as its detail: what loaded and what did not.</summary>
internal sealed class SoundFailureReport(PluginCatalog plugins, Playback playback, ReportLine report)
    : IReactTo<SoundFailed>
{
    public Task On(SoundFailed notice)
    {
        report.Say(notice.Message, PluginSummary.Text(plugins, playback.Sound.Failure));
        return Task.CompletedTask;
    }
}
