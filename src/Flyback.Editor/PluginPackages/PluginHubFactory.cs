using Flyback.Editor.Controls;

namespace Flyback.Editor.PluginPackages;

/// <summary>
/// Builds a plugins window for one opening, with the per-window dialog supplied
/// by the editor's container.
/// </summary>
internal sealed class PluginHubFactory(IDialog dialog)
{
    public PluginHub Create(
        PluginSite? site,
        Func<Task<IReadOnlyList<HubInstalled>>> installed,
        Func<SitePlugin, Action, Task<string?>> install,
        Func<HubInstalled, Task<string?>>? show = null,
        IReadOnlyList<SitePlugin>? needed = null,
        PluginRun? run = null) =>
        new(site, installed, install, dialog, show, needed, run);
}