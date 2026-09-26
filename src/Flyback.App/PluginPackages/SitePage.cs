namespace Flyback.App.PluginPackages;

/// <summary>One page of what the site found, and how many it found in all.</summary>
internal sealed record SitePage(IReadOnlyList<SitePlugin> Items, int Total, int Page, int PageSize)
{
    public bool More => Page * PageSize < Total;
}