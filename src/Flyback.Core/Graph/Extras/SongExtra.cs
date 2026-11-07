using System.Text;

namespace Flyback.Core.Graph.Extras;

/// <summary>The Song's parts: each named, each its own number of sections long.</summary>
public sealed record SongExtra(IReadOnlyList<SongPart> Default) : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    internal const string Name = "song";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <summary>The parts as stored, and none where it carries nothing.</summary>
    internal static List<SongPart> Of(NodeInstance node) =>
        Read<List<SongPart>>(node.StateOf(Name), []) ?? [];

    /// <summary>Replaces every part outright.</summary>
    public static void Set(NodeInstance node, IEnumerable<SongPart> parts) =>
        node.SetState(Name, Write(Tidy(parts)));

    /// <inheritdoc/>
    public override void Seed(NodeInstance node) => Set(node, Default);

    /// <summary>
    /// At most <see cref="NodeCatalog.MaxParts"/> parts of 1 to <see cref="SongModule.MaxSections"/>
    /// sections each, every name one the text can write and no two the same.
    /// </summary>
    internal static List<SongPart> Tidy(IEnumerable<SongPart?> parts)
    {
        var kept = new List<SongPart>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts.Where(part => part is not null).Take(NodeCatalog.MaxParts))
        {
            var levels = (part!.Levels ?? []).Take(SongModule.MaxSections).Select(level => level.Sane()).ToList();
            if (levels.Count == 0) levels.Add(new PartLevel(0f));

            kept.Add(new SongPart(Unique(Spelled(part.Name, kept.Count), taken), levels));
        }

        return kept;
    }

    /// <summary>
    /// Whether <paramref name="name"/> can be written after a dot: a letter or an underscore, then
    /// letters, digits and underscores.
    /// </summary>
    internal static bool Writable(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The output a part's name stands for, or -1.</summary>
    internal static int OutputOf(NodeInstance node, string name)
    {
        var parts = Of(node);

        for (var p = 0; p < parts.Count; p++)
            if (string.Equals(parts[p].Name, name, StringComparison.OrdinalIgnoreCase)) return p;

        return -1;
    }

    /// <summary>What output <paramref name="port"/> is called on this instance: a part's name, or null for the definition's.</summary>
    internal static string? OutputName(NodeInstance node, int port)
    {
        var parts = Of(node);
        return port < parts.Count && port < NodeCatalog.MaxParts ? parts[port].Name : null;
    }

    private static string Spelled(string? name, int index)
    {
        var spelled = new StringBuilder();

        foreach (var c in (name ?? "").Trim())
            spelled.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');

        var written = spelled.ToString().Trim('_');
        if (written.Length > 0 && char.IsAsciiDigit(written[0])) written = "_" + written;

        return written.Length == 0 ? $"part{index + 1}" : written;
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        var unique = name;
        for (var n = 2; !taken.Add(unique) || Reserved(unique); n++) unique = $"{name}{n}";
        return unique;
    }

    /// <summary>The Song's own outputs, which a part may not be called.</summary>
    private static bool Reserved(string name) =>
        name.ToLowerInvariant() is "progress" or "section" or "played"
        || (name.StartsWith("part_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out var part) && part is >= 1 and <= NodeCatalog.MaxParts);

    /// <inheritdoc/>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with { Parts = Tidy(Of(node)).Select(part => part.Levels).ToList() };

    /// <inheritdoc/>
    public override string Announce() =>
        $"  song  up to {NodeCatalog.MaxParts} named parts of up to {SongModule.MaxSections} sections, each looping at its own length — not knobs";

    /// <inheritdoc/>
    public override string Help =>
        "Each part's name and its level in each of its sections. A part comes round after its own last "
        + "section, so parts of different lengths drift against each other.";
}
