namespace Flyback.Editor.Site;

/// <summary>One page of what a site found, and how many it found in all.</summary>
internal sealed record SitePage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public bool More => Page * PageSize < Total;
}
