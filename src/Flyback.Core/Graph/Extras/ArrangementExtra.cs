namespace Flyback.Core.Graph.Extras;

/// <summary>The parts of an Arrangement and their level in each section.</summary>
public sealed record ArrangementExtra(IReadOnlyList<IReadOnlyList<PartLevel>> Default) : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    internal const string Name = "arrangement";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <summary>The parts as stored, one list of sections each, and none where it carries nothing.</summary>
    internal static List<List<PartLevel>> Of(NodeInstance node) =>
        Read<List<List<PartLevel>>>(node.StateOf(Name), []);

    /// <summary>Replaces every part outright, for the reason a tune is replaced outright.</summary>
    public static void Set(NodeInstance node, IEnumerable<IEnumerable<PartLevel>> parts) =>
        node.SetState(Name, Write(parts.Select(part => part.ToList()).ToList()));

    /// <inheritdoc/>
    public override void Seed(NodeInstance node) => Set(node, Default);

    /// <summary>
    /// At most <see cref="NodeCatalog.MaxParts"/> parts of at most
    /// <see cref="NodeCatalog.MaxSections"/> sections, every one as long as the
    /// longest, a short part held at nought to the end.
    /// </summary>
    internal static List<List<PartLevel>> Tidy(IEnumerable<IEnumerable<PartLevel>> parts)
    {
        var kept = parts
            .Take(NodeCatalog.MaxParts)
            .Select(part => part.Take(NodeCatalog.MaxSections).Select(level => level.Sane()).ToList())
            .ToList();

        var sections = kept.Count == 0 ? 0 : kept.Max(part => part.Count);

        foreach (var part in kept)
            while (part.Count < sections) part.Add(new PartLevel(0f));

        return sections == 0 ? [] : kept;
    }

    /// <inheritdoc/>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with { Parts = Tidy(Of(node)) };

    /// <inheritdoc/>
    public override string Announce() =>
        $"  arrangement  up to {NodeCatalog.MaxParts} parts of up to {NodeCatalog.MaxSections} sections — not knobs";

    /// <inheritdoc/>
    public override string Help =>
        "Each part's level in each section, one row a part. A level marked to glide travels "
        + "there from the section before's across its whole section.";
}
