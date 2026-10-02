using Flyback.Editor.Site;

namespace Flyback.Editor.PluginPackages;

/// <summary>A plugin the site lists.</summary>
/// <param name="Rating">Its stars on the site, which only the site gives.</param>
internal sealed record SitePlugin(
    string Id,
    ListedPlugin Plugin,
    IReadOnlyList<string> Builds,
    string Sha256,
    long Size,
    int Downloads,
    Uri File,
    Uri? Preview,
    SiteRating Rating);