namespace Flyback.App.Site;

/// <summary>One page of the presets the site found, and how many it found in all.</summary>
internal sealed record SitePresetPage(IReadOnlyList<SitePreset> Items, int Total, int Page, int PageSize)
{
    public bool More => Page * PageSize < Total;
}