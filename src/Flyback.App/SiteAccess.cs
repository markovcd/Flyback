using Flyback.App.PluginPackages;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App;

/// <summary>
/// Where the preset site is and what it is asked with: shared presets, shared plugins
/// and letters to the author all go through here.
/// </summary>
/// <param name="setup">Which site, or none for a window that reaches no site: every test that names none.</param>
/// <param name="http">What the site is asked with, registered under <see cref="Client"/>: <see cref="Shared"/>, or a test's own.</param>
internal sealed class SiteAccess(EditorSetup setup, [FromKeyedServices(SiteAccess.Client)] HttpClient http)
{
    /// <summary>The key the site's <see cref="HttpClient"/> is registered under, apart from any other the container holds.</summary>
    public const string Client = "site";

    /// <summary>Shared by every question put to the site, as an <see cref="HttpClient"/> is meant to be.</summary>
    public static HttpClient Shared => SharedClient.Value;

    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    public Uri? Root { get; } = setup.PresetSite;

    public HttpClient Http { get; } = http;

    /// <summary>What the gallery asks for shared presets, or null where there is no site.</summary>
    public PresetSite? Presets() => Root is null ? null : new PresetSite(Http, Root);

    /// <summary>What the plugins window asks for shared plugins, or null where there is no site.</summary>
    public PluginSite? Plugins() => Root is null ? null : new PluginSite(Http, Root);
}
