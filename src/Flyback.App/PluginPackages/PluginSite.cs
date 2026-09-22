using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
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

/// <summary>A plugin the site lists.</summary>
internal sealed record SitePlugin(
    string Id,
    ListedPlugin Plugin,
    IReadOnlyList<string> Builds,
    string Sha256,
    long Size,
    int Downloads,
    Uri File,
    Uri? Preview);

/// <summary>One page of what the site found, and how many it found in all.</summary>
internal sealed record SitePage(IReadOnlyList<SitePlugin> Items, int Total, int Page, int PageSize)
{
    public bool More => Page * PageSize < Total;
}

/// <summary>The plugins shared on the preset site, as <c>/api/v1/plugins</c> lists them for this system.</summary>
internal sealed class PluginSite(HttpClient http, Uri root)
{
    public Uri Root { get; } = root;

    /// <summary>
    /// The <paramref name="page"/>th page of published plugins that build for this system
    /// and match <paramref name="search"/> and <paramref name="tag"/>. Throws where the
    /// site cannot be reached or answers with something else.
    /// </summary>
    public async Task<SitePage> SearchAsync(string? search, string? tag, int page, CancellationToken cancel)
    {
        var query = new List<string> { $"page={page.ToString(CultureInfo.InvariantCulture)}" };

        if (!string.IsNullOrWhiteSpace(search)) query.Add("q=" + Uri.EscapeDataString(search.Trim()));
        if (!string.IsNullOrEmpty(tag)) query.Add("tag=" + Uri.EscapeDataString(tag));
        if (PluginPackage.ThisPlatform is { Length: > 0 } platform) query.Add("platform=" + platform);

        using var response = await http.GetAsync(new Uri(Root, "api/v1/plugins?" + string.Join('&', query)), cancel);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);

        return Read(document.RootElement, Root);
    }

    /// <summary>
    /// The package, refused unless it hashes to what the site listed.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not the ones listed.</exception>
    public async Task<byte[]> DownloadAsync(SitePlugin plugin, CancellationToken cancel)
    {
        var bytes = await http.GetByteArrayAsync(plugin.File, cancel);

        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), plugin.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("What was downloaded is not the package the site lists.");

        return bytes;
    }

    /// <summary>The plugin's preview image, or null where it has none or it cannot be fetched.</summary>
    public async Task<byte[]?> PreviewAsync(SitePlugin plugin, CancellationToken cancel)
    {
        if (plugin.Preview is not { } preview) return null;

        try
        {
            return await http.GetByteArrayAsync(preview, cancel);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>The listing, read. A plugin missing its id, name or hash is left out.</summary>
    internal static SitePage Read(JsonElement found, Uri root)
    {
        var items = new List<SitePlugin>();

        if (found.TryGetProperty("items", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in listed.EnumerateArray())
            {
                if (Text(item, "id") is not { Length: > 0 } id
                    || Text(item, "name") is not { Length: > 0 } name
                    || Text(item, "sha256") is not { Length: 64 } sha256
                    || Text(item, "file") is not { } file
                    || !Uri.TryCreate(root, file, out var fileUri))
                    continue;

                var plugin = new ListedPlugin(
                    Text(item, "assembly") ?? string.Empty,
                    name,
                    Text(item, "version") ?? string.Empty,
                    Text(item, "author") ?? string.Empty,
                    Text(item, "description") ?? string.Empty,
                    Texts(item, "tags"),
                    item.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array
                        ? [.. modules.EnumerateArray().Select(m => Text(m, "name")).OfType<string>()]
                        : []);

                items.Add(new SitePlugin(
                    id,
                    plugin,
                    Texts(item, "builds"),
                    sha256,
                    Number(item, "size"),
                    (int)Number(item, "downloads"),
                    fileUri,
                    Text(item, "preview") is { } preview && Uri.TryCreate(root, preview, out var previewUri) ? previewUri : null));
            }
        }

        return new SitePage(items, (int)Number(found, "total"), Math.Max(1, (int)Number(found, "page")), Math.Max(1, (int)Number(found, "pageSize")));
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> Texts(JsonElement item, string name) =>
        item.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? [.. values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]
            : [];

    private static long Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
}
