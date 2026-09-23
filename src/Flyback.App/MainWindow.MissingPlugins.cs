using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>Offering the plugins a patch names that this Flyback does not have (ADR-0135).</summary>
public sealed partial class MainWindow
{
    /// <summary>How long the site is given before the offer is dropped and the refusal stands alone.</summary>
    private static readonly TimeSpan Looking = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Offers what <paramref name="loaded"/> was refused for, where the plugin site has a
    /// build of it for this system. Silent where it has none or there is no site: the
    /// refusal has been reported already, and an offer of nothing is worse than none.
    /// </summary>
    /// <param name="open">
    /// What this patch was, so that it can be opened again: a file on disk, or a preset
    /// the site shared. Installing from the offer restarts Flyback, and this is what it
    /// opens when it comes back up with the plugin.
    /// </param>
    private async Task OfferMissingPluginsAsync(PatchLoad loaded, Reopen? open = null)
    {
        if (loaded.MissingProviders.Count == 0 || presetSite is null) return;

        using var cancel = new CancellationTokenSource(Looking);

        var site = new PluginSite(SiteHttp ?? SiteClient.Value, presetSite);
        var found = await MissingPlugins.FoundAsync(site, loaded, cancel.Token);

        if (found.Count == 0) return;

        if (!await this.ShowDialog<bool>(MissingPluginsView.Title, MissingPluginsView.View(found))) return;

        // Live only while that window is up: coming back from it is nothing having been
        // installed, or something having been that a restart was not asked for.
        refused = open;
        awaited = found;

        try
        {
            await ShowPluginsAsync(found);
        }
        finally
        {
            refused = null;
            awaited = [];
        }
    }
}
