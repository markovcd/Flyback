using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// What a plugin's build says it is and what it can do, as the install dialog shows it:
/// the plugin assembly's own attributes, and what the code of every assembly in the build
/// names. Every string is cleaned for showing.
/// </summary>
/// <param name="Assembly">The plugin assembly's name, which is also the folder it is installed into.</param>
/// <param name="Modules">The modules the plugin assembly declares, which are all it may register.</param>
/// <param name="Tags">Its <c>AssemblyMetadata("Tags", …)</c>, tidied as a patch's tags are.</param>
/// <param name="Adds">What it can register: modules, presets, a sound output and so on.</param>
/// <param name="Reaches">What outside Flyback its code names, from any assembly in the build.</param>
/// <param name="Preview">The image it embeds as <c>preview.png</c> or <c>preview.webp</c>, or null for none.</param>
/// <param name="Compiled">
/// Every assembly in the build and what it was compiled against, the plugin's first.
/// A helper the plugin carries is compiled against the contract as much as the plugin
/// is, and is missed only when it is first called.
/// </param>
internal sealed partial record PluginDescription(
    string Assembly,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Adds,
    IReadOnlyList<string> Reaches,
    PluginPreview? Preview,
    IReadOnlyList<(string Assembly, IReadOnlyList<AssemblyName> References)> Compiled,
    IReadOnlyList<DeclaredModule> Modules)
{
    /// <summary>
    /// Why this build would not be loaded, or null where it would: an assembly compiled
    /// against a contract this Flyback does not offer, or a plugin adding modules without
    /// declaring one.
    /// </summary>
    public string? Refusal() =>
        ContractRefusal()
        ?? (Adds.Contains(AssemblyFacts.ModulesAdded) && Modules.Count == 0
            ? "It adds modules without declaring any with [assembly: FlybackModule(id, name)], so Flyback would refuse it."
            : null);

    /// <summary>
    /// The versions of <c>Flyback.Plugins</c> and <c>Flyback.Core</c> the plugin assembly
    /// was compiled against, as the dialog shows them, or an empty string for neither.
    /// </summary>
    public string BuiltAgainst => string.Join(", ", Compiled[0].References
        .Where(r => ContractVersion.IsContract(r.Name))
        .OrderBy(r => r.Name, StringComparer.Ordinal)
        .Select(r => $"{r.Name} {r.Version?.ToString(3)}"));

    /// <summary>
    /// Why no assembly in the build may be loaded by this Flyback, naming it where it is
    /// not the plugin's own, or null where every one may.
    /// </summary>
    public string? ContractRefusal()
    {
        foreach (var (assembly, references) in Compiled)
        {
            if (ContractVersion.Reason(references) is not { } reason) continue;

            return assembly == Assembly
                ? reason
                : $"{assembly}.dll, which the plugin carries, was {char.ToLowerInvariant(reason[0])}{reason[1..]}";
        }

        return null;
    }

    /// <summary>
    /// Describes a build from its files, each by its path inside the build and a way to
    /// open it.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Where the build has more than one plugin assembly at its top, or the plugin's
    /// preview is not one image of the kind and size a preview may be.
    /// </exception>
    /// <returns>Null for a build with no plugin assembly at its top.</returns>
    public static PluginDescription? Of(IEnumerable<(string Path, Func<Stream> Open)> files)
    {
        var entries = new List<AssemblyFacts>();
        var compiled = new List<AssemblyFacts>();
        var adds = new SortedSet<string>(StringComparer.Ordinal);
        var reaches = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, open) in files)
        {
            if (!Binary(path)) continue;

            AssemblyFacts? facts;

            using (var stream = open()) facts = AssemblyFacts.Of(stream);

            if (facts is null)
            {
                reaches.Add(AssemblyFacts.NativeCode);
                continue;
            }

            if (PluginLoadContext.IsHostOwned(facts.Name)) continue;

            adds.UnionWith(facts.Adds);
            reaches.UnionWith(facts.Reaches);
            compiled.Add(facts);

            if (facts.IsPlugin && !path.Contains('/')) entries.Add(facts);
        }

        if (entries.Count == 0) return null;

        if (entries.Count > 1)
            throw new InvalidDataException($"A build holds {entries.Count} plugin assemblies, {string.Join(" and ", entries.Select(e => e.Name))}, where a package has one.");

        var entry = entries[0];

        if (!ValidFolder(entry.Name))
            throw new InvalidDataException($"Its plugin assembly, {Line(entry.Name, 80)}, has a name a folder cannot safely take.");

        return new PluginDescription(
            entry.Name,
            Line(Named(entry, "Product") ?? Named(entry, "Title") ?? entry.Name, 64),
            Line(VersionOf(entry), 32),
            Line(Named(entry, "Company"), 64),
            Paragraph(Named(entry, "Description"), 1000),
            TagsOf(entry),
            [.. adds],
            [.. reaches],
            PluginPreview.Of(entry.Previews),
            [.. compiled.OrderBy(facts => facts == entry ? 0 : 1).Select(facts => (facts.Name, facts.References))],
            entry.Modules);
    }

    /// <summary>The <c>Tags</c> pair split at commas and semicolons.</summary>
    private static List<string> TagsOf(AssemblyFacts facts) =>
        facts.Metadata.TryGetValue("Tags", out var tags)
            ? Patch.TidiedTags(tags.Split([',', ';']).Select(t => Line(t, 64))) ?? []
            : [];

    /// <summary>Describes a plugin folder already on disk.</summary>
    public static PluginDescription? OfFolder(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        try
        {
            return Of(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => (Path.GetRelativePath(folder, file).Replace('\\', '/'), (Func<Stream>)(() => File.OpenRead(file)))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>What may be code: a managed assembly, or a native library for any of the three systems.</summary>
    private static bool Binary(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
        || path.Contains(".so.", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>An attribute's value, or null where it is missing or only the SDK's default, the assembly's own name.</summary>
    private static string? Named(AssemblyFacts facts, string attribute) =>
        facts.Attributes.TryGetValue(attribute, out var value) && value.Length > 0 && value != facts.Name ? value : null;

    /// <summary>The informational version without the commit the SDK appends, else the assembly's version.</summary>
    private static string VersionOf(AssemblyFacts facts) =>
        facts.Attributes.TryGetValue("InformationalVersion", out var informational) && informational.Length > 0
            ? informational.Split('+')[0]
            : facts.Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Whether <paramref name="name"/> is safe as a folder's name on all three systems:
    /// ASCII, not starting or ending with a dot, and not a name Windows keeps for a device.
    /// </summary>
    public static bool ValidFolder(string name) => FolderPattern().IsMatch(name) && !PluginPackage.Reserved(name);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$")]
    private static partial Regex FolderPattern();

    /// <summary>One line of text as it may be shown, with nothing in it that draws something other than itself.</summary>
    internal static string Line(string? text, int longest) => Clean(text, longest, lines: false);

    /// <summary>A few paragraphs as they may be shown: <see cref="Line"/>, keeping its line breaks.</summary>
    internal static string Paragraph(string? text, int longest) => Clean(text, longest, lines: true);

    /// <summary>
    /// Drops the characters that change how the rest are drawn — a right-to-left
    /// override that turns <c>exe.txt</c> round, a zero-width joiner — and cuts the
    /// text to <paramref name="longest"/> characters.
    /// </summary>
    private static string Clean(string? text, int longest, bool lines)
    {
        if (text is null) return string.Empty;

        var kept = new StringBuilder();

        foreach (var rune in text.EnumerateRunes())
        {
            if (lines && rune.Value == '\n')
            {
                kept.Append('\n');
                continue;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator:
                    kept.Append(' ');
                    break;
                case UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned:
                    break;
                default:
                    kept.Append(rune.ToString());
                    break;
            }
        }

        var cleaned = Blank().Replace(kept.ToString(), " ");

        if (lines) cleaned = Gap().Replace(cleaned.Replace(" \n", "\n").Replace("\n ", "\n"), "\n\n");

        cleaned = cleaned.Trim();

        var cut = new StringInfo(cleaned);

        return cut.LengthInTextElements <= longest ? cleaned : cut.SubstringByTextElements(0, longest - 1) + "…";
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex Blank();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex Gap();
}
