using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flyback.App.PluginPackages;

/// <summary>
/// What a plugin package says about itself in its <c>plugin.json</c>. Whoever built
/// the package wrote every word of it, and nothing here has checked one.
/// </summary>
/// <param name="Id">Also the name of the folder the plugin is installed into.</param>
/// <param name="Website">An http or https address with its host spelled out in ASCII, or null.</param>
internal sealed partial record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    string? Website)
{
    private const int LongestId = 64;

    /// <exception cref="InvalidDataException">Where the text is not a manifest, saying why.</exception>
    public static PluginManifest Parse(ReadOnlyMemory<byte> json)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4, AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"Its {PluginPackage.ManifestName} is not JSON with one of each field.");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Its {PluginPackage.ManifestName} is not a JSON object.");

            var id = Field(root, "id") ?? throw Missing("id");

            if (!ValidId(id))
                throw new InvalidDataException(
                    $"Its id is not one a folder can be named after: up to {LongestId} letters, digits, dots, dashes and underscores.");

            return new PluginManifest(
                id,
                Required(root, "name", 64),
                Required(root, "version", 32),
                Line(Field(root, "author"), 64),
                Paragraph(Field(root, "description"), 1000),
                Address(Field(root, "website")));
        }
    }

    public byte[] ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("id", Id);
            writer.WriteString("name", Name);
            writer.WriteString("version", Version);
            writer.WriteString("author", Author);
            writer.WriteString("description", Description);
            if (Website is not null) writer.WriteString("website", Website);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Whether <paramref name="id"/> is safe as a folder's name on all three systems:
    /// ASCII, not starting or ending with a dot, and not a name Windows keeps for a device.
    /// </summary>
    public static bool ValidId(string id) => IdPattern().IsMatch(id) && !PluginPackage.Reserved(id);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$")]
    private static partial Regex IdPattern();

    private static string? Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Required(JsonElement root, string name, int longest) =>
        Line(Field(root, name), longest) is { Length: > 0 } text ? text : throw Missing(name);

    private static InvalidDataException Missing(string field) =>
        new($"Its {PluginPackage.ManifestName} does not say what its {field} is.");

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

    /// <summary>
    /// An http or https address, with the host in ASCII so a lookalike letter shows as
    /// <c>xn--</c>, and no user name in front of the host to pass one site off as another.
    /// </summary>
    private static string? Address(string? text)
    {
        if (text is null || text.Length > 200) return null;

        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return null;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;

        if (uri.UserInfo.Length > 0) return null;

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";

        return Line($"{uri.Scheme}://{uri.IdnHost}{port}{uri.PathAndQuery}", 200);
    }
}
