using System.Text.Json;
using Flyback.Core.Graph;

namespace Flyback.App.PluginPackages;

/// <summary>
/// The plugins a patch names and this Flyback does not have, looked for on the plugin
/// site (ADR-0135).
/// </summary>
internal static class MissingPlugins
{
    /// <summary>
    /// The site's plugin for each provider <paramref name="loaded"/> is short of, each
    /// listed once, and empty where the site has none of them or does not answer.
    /// </summary>
    /// <remarks>
    /// Asked for by the type id of a module the patch actually holds rather than by the
    /// plugin's name: an id names one plugin or none, and a patch that will not open is
    /// the worst place to offer a plugin that merely reads alike. A provider with no
    /// module of its own left in the patch is not asked about.
    /// </remarks>
    public static async Task<IReadOnlyList<SitePlugin>> FoundAsync(PluginSite site, PatchLoad loaded, CancellationToken cancel)
    {
        var found = new List<SitePlugin>();

        foreach (var provider in loaded.MissingProviders)
        {
            if (ModuleOf(loaded, provider) is not { } module) continue;

            try
            {
                var page = await site.SearchAsync(search: null, tag: null, page: 1, cancel, module);

                if (page.Items is [var plugin, ..] && found.TrueForAll(f => f.Id != plugin.Id)) found.Add(plugin);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                // A site that is down or slow leaves the patch refused, which it is anyway.
                return found;
            }
        }

        return found;
    }

    /// <summary>A module in the patch that <paramref name="provider"/> is the plugin for, or null where none is left.</summary>
    public static string? ModuleOf(PatchLoad loaded, ModuleProvider provider) =>
        loaded.UnknownModules.FirstOrDefault(m => m.StartsWith(provider.Id + ".", StringComparison.OrdinalIgnoreCase));
}
