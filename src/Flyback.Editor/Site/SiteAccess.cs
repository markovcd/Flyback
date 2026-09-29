using Flyback.App.PluginPackages;

namespace Flyback.App.Site;

/// <summary>
/// Where the preset site is and what it is asked with: shared presets, shared plugins
/// and letters to the author all go through here.
/// </summary>
/// <param name="setup">Which site, or none for a window that reaches no site: every test that names none.</param>
/// <param name="clients">Makes the <see cref="Client"/> the site is asked with, which a test configures with its own handler.</param>
internal sealed class SiteAccess(EditorSetup setup, IHttpClientFactory clients)
{
    /// <summary>The name of the site's <see cref="HttpClient"/>, apart from any other the container makes.</summary>
    public const string Client = "site";

    public Uri? Root { get; } = setup.PresetSite;

    public HttpClient Http => clients.CreateClient(Client);

    /// <summary>What the gallery asks for shared presets, or null where there is no site.</summary>
    public PresetSite? Presets() => Root is null ? null : new PresetSite(Http, Root);

    /// <summary>What the plugins window asks for shared plugins, or null where there is no site.</summary>
    public PluginSite? Plugins() => Root is null ? null : new PluginSite(Http, Root);
}
