using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flyback.Plugins.Hosting;

/// <summary>The module packs shared on the preset site, as <c>/api/v1/packs</c> lists them.</summary>
internal sealed class PackSite(HttpClient http, Uri root)
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public Uri Root { get; } = root;

    /// <summary>
    /// The <paramref name="page"/>th page of published packs matching every word of
    /// <paramref name="search"/>, each at its newest version. Throws where the site cannot be
    /// reached or answers with something else.
    /// </summary>
    /// <param name="pack">A pack's id, listing every published version of that pack instead.</param>
    /// <param name="module">A module's type id, narrowing it to the pack that offers it.</param>
    public async Task<SitePacks> SearchAsync(string? search, int page, CancellationToken cancel, string? pack = null, string? module = null)
    {
        var query = new List<string> { $"page={page.ToString(CultureInfo.InvariantCulture)}" };

        if (!string.IsNullOrWhiteSpace(search)) query.Add("q=" + Uri.EscapeDataString(search.Trim()));
        if (!string.IsNullOrEmpty(pack)) query.Add("pack=" + Uri.EscapeDataString(pack));
        if (!string.IsNullOrEmpty(module)) query.Add("module=" + Uri.EscapeDataString(module));

        using var response = await http.GetAsync(new Uri(Root, "api/v1/packs?" + string.Join('&', query)), cancel);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);

        return Read(document.RootElement, Root);
    }

    /// <summary>The newest published version of the pack <paramref name="pack"/>, or null where the site lists none.</summary>
    public async Task<SitePack?> NewestAsync(string pack, CancellationToken cancel) =>
        (await SearchAsync(null, 1, cancel, pack: pack)).Items.FirstOrDefault(listed => listed.Pack == pack);

    /// <summary>The pack's text, refused unless it is the size and hash the site listed.</summary>
    /// <exception cref="InvalidDataException">The bytes are not the ones listed, or not text.</exception>
    public async Task<string> DownloadAsync(SitePack pack, CancellationToken cancel)
    {
        using var response = await http.GetAsync(pack.File, HttpCompletionOption.ResponseHeadersRead, cancel);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > InstalledPacks.MostBytes)
            throw new InvalidDataException($"The pack is over {InstalledPacks.MostBytes / 1024} KB.");

        var bytes = await Limited(await response.Content.ReadAsStreamAsync(cancel), cancel);

        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), pack.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("What was downloaded is not the pack the site lists.");

        try
        {
            return Strict.GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("What was downloaded is not a pack's text.");
        }
    }

    private static async Task<byte[]> Limited(Stream stream, CancellationToken cancel)
    {
        await using var _ = stream;
        using var kept = new MemoryStream();
        var buffer = new byte[81920];

        int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
        {
            if (kept.Length + read > InstalledPacks.MostBytes)
                throw new InvalidDataException($"The pack is over {InstalledPacks.MostBytes / 1024} KB.");

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }

    /// <summary>The listing, read. A pack missing its id, pack, version or hash, or whose file is on another site, is left out.</summary>
    internal static SitePacks Read(JsonElement found, Uri root)
    {
        var items = new List<SitePack>();

        if (found.ValueKind == JsonValueKind.Object && found.TryGetProperty("items", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in listed.EnumerateArray())
            {
                if (Text(item, "id") is not { Length: > 0 } id
                    || Text(item, "pack") is not { Length: > 0 } pack
                    || Text(item, "version") is not { Length: > 0 } version
                    || Text(item, "sha256") is not { Length: 64 } sha256
                    || Text(item, "file") is not { } file
                    || !Uri.TryCreate(root, file, out var fileUri)
                    || Uri.Compare(fileUri, root, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
                    continue;

                var modules = item.TryGetProperty("modules", out var offered) && offered.ValueKind == JsonValueKind.Array
                    ? offered.EnumerateArray()
                        .Where(m => Text(m, "id") is { Length: > 0 })
                        .Select(m => (Text(m, "id")!, Text(m, "name") ?? Text(m, "id")!))
                        .ToList()
                    : [];

                items.Add(new SitePack(id, pack, version, modules, sha256, Number(item, "size"), (int)Number(item, "downloads"), fileUri));
            }
        }

        return new SitePacks(items, (int)Number(found, "total"), Math.Max(1, (int)Number(found, "page")), Math.Max(1, (int)Number(found, "pageSize")));
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Number(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
}
