using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>What the plugins window shows of a plugin, wherever it came from.</summary>
internal sealed record ListedPlugin(
    string Assembly,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Modules)
{
    public static ListedPlugin Of(PluginDescription plugin) => new(
        plugin.Assembly, plugin.Name, plugin.Version, plugin.Author, plugin.Description,
        plugin.Tags, [.. plugin.Modules.Select(m => m.Name)]);

    /// <summary>
    /// Whether every word of <paramref name="search"/> is somewhere in its name, author,
    /// description, assembly, tags or modules, and it carries <paramref name="tag"/>: what
    /// the site's own search matches.
    /// </summary>
    public bool Matches(string? search, string? tag = null)
    {
        if (!string.IsNullOrEmpty(tag) && !Tags.Contains(tag, StringComparer.Ordinal)) return false;

        var words = (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.All(word =>
            Has(Name, word) || Has(Author, word) || Has(Description, word) || Has(Assembly, word)
            || Tags.Any(t => Has(t, word)) || Modules.Any(m => Has(m, word)));

        static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}