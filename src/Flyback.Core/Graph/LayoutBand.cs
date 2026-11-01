namespace Flyback.Core.Graph;

/// <summary>
/// A level the text opened inside a box, which the layout gives a band of its own:
/// the modules it placed itself, and the levels opened inside it.
/// </summary>
/// <param name="Label">What its shelf chip reads.</param>
internal sealed record LayoutBand(string Label, IReadOnlyList<Guid> Members, IReadOnlyList<LayoutBand> Inside)
{
    /// <summary>Every module in this band and in the bands inside it.</summary>
    public IEnumerable<Guid> All => Members.Concat(Inside.SelectMany(band => band.All));

    /// <summary>The band whose modules are exactly <paramref name="members"/>, searched at every depth.</summary>
    public static LayoutBand? Matching(IReadOnlyList<LayoutBand> bands, IReadOnlyCollection<Guid> members)
    {
        foreach (var band in bands)
        {
            var all = band.All.ToHashSet();

            if (all.Count == members.Count && members.All(all.Contains)) return band;

            if (Matching(band.Inside, members) is { } inner) return inner;
        }

        return null;
    }
}
