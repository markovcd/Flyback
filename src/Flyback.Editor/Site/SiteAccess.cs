using Flyback.Editor.PluginPackages;

namespace Flyback.Editor.Site;

/// <summary>
/// Where the preset site is and what it is asked with: shared presets, shared plugins
/// and letters to the author all go through here.
/// </summary>
/// <param name="host">Which site, or none for a window that reaches no site: every test that names none.</param>
/// <param name="folders">Where the shared presets opened are kept.</param>
/// <param name="clients">Makes the <see cref="Client"/> the site is asked with, which a test configures with its own handler.</param>
internal sealed class SiteAccess(EditorHost host, EditorFolders folders, IHttpClientFactory clients)
{
    /// <summary>The name of the site's <see cref="HttpClient"/>, apart from any other the container makes.</summary>
    public const string Client = "site";

    public Uri? Root { get; } = host.PresetSite;

    public HttpClient Http => clients.CreateClient(Client);

    /// <summary>The shared presets opened before, for when the site does not answer.</summary>
    public KeptSharedPresets Kept { get; } = new(folders.SharedPresetFolder);

    /// <summary>What the gallery asks for shared presets, or null where there is no site.</summary>
    public PresetSite? Presets() => Root is null ? null : new PresetSite(Http, Root);

    /// <summary>What the plugins window asks for shared plugins, or null where there is no site or a page, which installs none.</summary>
    public PluginSite? Plugins() => Root is null || host.InPage ? null : new PluginSite(Http, Root);
}
