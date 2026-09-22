using System.Globalization;
using System.Text.Json;

namespace Flyback.App;

/// <summary>A preset the site lists.</summary>
/// <param name="FileName">What it was shared as, whose extension says whether it is a bundle.</param>
/// <param name="Still">A frame of it, once the site has rendered one.</param>
/// <param name="Rating">Its stars on the site, which only the site gives.</param>
internal sealed record SitePreset(
    string Id,
    string Name,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    string FileName,
    Uri File,
    Uri? Still,
    SiteRating Rating);

/// <summary>One page of the presets the site found, and how many it found in all.</summary>
internal sealed record SitePresetPage(IReadOnlyList<SitePreset> Items, int Total, int Page, int PageSize)
{
    public bool More => Page * PageSize < Total;
}

/// <summary>The presets shared on the preset site, as <c>/api/v1/presets</c> lists them.</summary>
internal sealed class PresetSite(HttpClient http, Uri root)
{
    /// <summary>The preset site as it runs locally.</summary>
    public static readonly Uri Local = new("http://localhost:8790/");

    public Uri Root { get; } = root;

    /// <summary>
    /// The <paramref name="page"/>th page of published presets matching every word of
    /// <paramref name="search"/>. Throws where the site cannot be reached or answers with
    /// something else.
    /// </summary>
    public async Task<SitePresetPage> SearchAsync(string? search, int page, CancellationToken cancel)
    {
        var query = $"api/v1/presets?page={page.ToString(CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(search)) query += "&q=" + Uri.EscapeDataString(search.Trim());

        using var response = await http.GetAsync(new Uri(Root, query), cancel);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);

        return Read(document.RootElement, Root);
    }

    /// <summary>The preset's file, as it was shared.</summary>
    public Task<byte[]> DownloadAsync(SitePreset preset, CancellationToken cancel) =>
        http.GetByteArrayAsync(preset.File, cancel);

    /// <summary>Reports the preset to the site's admin. Throws where the site does not take it.</summary>
    public Task ReportAsync(SitePreset preset, string reason, string? details, CancellationToken cancel) =>
        SiteReports.SendAsync(http, Root, "preset", preset.Id, reason, details, cancel);

    /// <summary>The preset's still, or null where it has none or it cannot be fetched.</summary>
    public async Task<byte[]?> StillAsync(SitePreset preset, CancellationToken cancel)
    {
        if (preset.Still is not { } still) return null;

        try
        {
            return await http.GetByteArrayAsync(still, cancel);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>The listing, read. A preset missing its id, name or file is left out.</summary>
    internal static SitePresetPage Read(JsonElement found, Uri root)
    {
        var items = new List<SitePreset>();

        if (found.TryGetProperty("items", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in listed.EnumerateArray())
            {
                if (Text(item, "id") is not { Length: > 0 } id
                    || Text(item, "name") is not { Length: > 0 } name
                    || Text(item, "file") is not { } file
                    || !Uri.TryCreate(root, file, out var fileUri))
                    continue;

                var still = item.TryGetProperty("media", out var media) && Text(media, "still") is { } path && Uri.TryCreate(root, path, out var stillUri)
                    ? stillUri
                    : null;

                items.Add(new SitePreset(
                    id,
                    name,
                    Text(item, "author") ?? string.Empty,
                    Text(item, "description") ?? string.Empty,
                    item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                        ? [.. tags.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!)]
                        : [],
                    Text(item, "fileName") ?? string.Empty,
                    fileUri,
                    still,
                    SiteRating.Read(item)));
            }
        }

        return new SitePresetPage(items, Number(found, "total"), Math.Max(1, Number(found, "page")), Math.Max(1, Number(found, "pageSize")));
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
