namespace Flyback.Plugins.Hosting;

/// <summary>One page of the packs the preset site lists.</summary>
internal sealed record SitePacks(IReadOnlyList<SitePack> Items, int Total, int Page, int PageSize)
{
    public bool More => Page * PageSize < Total;
}
