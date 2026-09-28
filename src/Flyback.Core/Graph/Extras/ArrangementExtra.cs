using System.Globalization;

namespace Flyback.Core.Graph.Extras;

/// <summary>The parts of an Arrangement and their level in each section.</summary>
public sealed record ArrangementExtra(IReadOnlyList<IReadOnlyList<PartLevel>> Default) : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "arrangement";

    public override string Key => Name;

    /// <summary>The parts as stored, one list of sections each, and none where it carries nothing.</summary>
    public static List<List<PartLevel>> Of(NodeInstance node) =>
        Read<List<List<PartLevel>>>(node.StateOf(Name), []);

    /// <summary>Replaces every part outright, for the reason a tune is replaced outright.</summary>
    public static void Set(NodeInstance node, IEnumerable<IEnumerable<PartLevel>> parts) =>
        node.SetState(Name, Write(parts.Select(part => part.ToList()).ToList()));

    public override void Seed(NodeInstance node) => Set(node, Default);

    /// <summary>
    /// At most <see cref="NodeCatalog.MaxParts"/> parts of at most
    /// <see cref="NodeCatalog.MaxSections"/> sections, every one as long as the
    /// longest, a short part held at nought to the end.
    /// </summary>
    public static List<List<PartLevel>> Tidy(IEnumerable<IEnumerable<PartLevel>> parts)
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

    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with { Parts = Tidy(Of(node)) };

    public override string Report(NodeInstance node)
    {
        if (Tidy(Of(node)) is not { Count: > 0 } parts) return "It has no parts.";

        var written = parts.Select((part, i) =>
            $"part {i + 1}: {string.Join(" ", part.Select(Written))}");

        return $"{parts[0].Count} sections. {string.Join("; ", written)}.";
    }

    public override string Announce() =>
        $"  arrangement  up to {NodeCatalog.MaxParts} parts of up to {NodeCatalog.MaxSections} sections — not knobs";

    public override string Help =>
        "Each part's level in each section, one row a part. A level marked to glide travels "
        + "there from the section before's across its whole section.";

    /// <summary>A level as the text language writes it: '>' in front where it glides.</summary>
    private static string Written(PartLevel level) =>
        (level.Glides ? ">" : "") + level.Value.ToString(CultureInfo.InvariantCulture);
}
