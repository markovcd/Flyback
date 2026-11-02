using System.Globalization;
using System.Text.Json;
using Avalonia.Media;

namespace Flyback.Ui.Controls;

/// <summary>
/// A color scheme written as JSON: a <c>format</c>, a <c>name</c>, the <c>grounds</c> and
/// the <c>text</c> by role, and the <c>accent</c>, each color <c>#RRGGBB</c>, and
/// <c>light</c> set true where the grounds are pale.
/// </summary>
/// <remarks>
/// <c>format</c> rises only when an older reader would get a file wrong: a key renamed or
/// meaning something new. A color added later is worked out from the others in a file
/// that lacks it, so an older file never needs touching.
/// </remarks>
internal static class SchemeFile
{
    /// <summary>The layout this build writes and reads.</summary>
    public const int Format = 1;

    /// <summary>Far more than seventeen colors need; anything larger is not a scheme.</summary>
    public const int MostBytes = 64 * 1024;

    public const int LongestName = 40;

    /// <summary>How far from the ground a file's label is taken for the bright text it does not name.</summary>
    public const double BrightUnless = 0.7;

    private static readonly JsonDocumentOptions Reading = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 4,
    };

    /// <summary>Every color a file holds, where it holds it, in the order <see cref="Scheme"/> takes them.</summary>
    private static readonly (string? Section, string Key)[] Keys =
    [
        ("grounds", "edge"),
        ("grounds", "outline"),
        ("grounds", "window"),
        ("grounds", "canvas"),
        ("grounds", "panel"),
        ("grounds", "toolbar"),
        ("grounds", "grid"),
        ("grounds", "gridMajor"),
        ("grounds", "module"),
        ("grounds", "moduleSelected"),
        ("grounds", "separator"),
        ("text", "label"),
        ("text", "value"),
        ("text", "normalled"),
        ("text", "muted"),
        ("text", "inactive"),
        (null, "accent"),
    ];

    /// <summary>
    /// Reads one scheme, or null where the text is not one. Never throws; what was wrong
    /// is added to <paramref name="problems"/>.
    /// </summary>
    /// <param name="fallback">
    /// Where a color the file lacks or gets wrong is taken from, or null to refuse the
    /// file for it, as a shipped one is.
    /// </param>
    public static Scheme? Read(string json, Scheme? fallback, ICollection<string> problems)
    {
        if (json.Length > MostBytes)
        {
            problems.Add($"is larger than {MostBytes / 1024} KB, so it is not a color scheme.");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, Reading);

            return Read(document.RootElement, fallback, problems);
        }
        catch (JsonException ex)
        {
            problems.Add($"is not JSON: {ex.Message}");
            return null;
        }
    }

    private static Scheme? Read(JsonElement root, Scheme? fallback, ICollection<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add("is not a color scheme: it holds no object.");
            return null;
        }

        if (!root.TryGetProperty("format", out var format) || !format.TryGetInt32(out var version) || version < 1)
        {
            problems.Add("has no format number, so it is not a color scheme.");
            return null;
        }

        if (version > Format)
        {
            problems.Add($"is format {version}, from a newer Flyback; this one reads format {Format}.");
            return null;
        }

        var name = root.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String
            ? named.GetString()!.Trim()
            : string.Empty;

        if (name.Length == 0 || name.Length > LongestName || name.Any(char.IsControl))
        {
            problems.Add($"needs a name of 1 to {LongestName} characters.");
            return null;
        }

        var colors = new Color[Keys.Length];
        var standIns = fallback is null ? null : Colors(fallback);

        for (var i = 0; i < Keys.Length; i++)
        {
            var (section, key) = Keys[i];
            var label = section is null ? key : $"{section}.{key}";

            if (Hex(root, section, key) is { } color)
            {
                colors[i] = color;
                continue;
            }

            if (standIns is null)
            {
                problems.Add($"{label} is missing or not a #RRGGBB color.");
                return null;
            }

            problems.Add($"{label} is missing or not a #RRGGBB color, so {fallback!.Name}'s is used.");
            colors[i] = standIns[i];
        }

        var light = Flag(root, "light", problems);

        var bright = Optional(root, "text", "bright", problems)
            ?? Controls.Colors.Blend(colors[11], light ? Avalonia.Media.Colors.Black : Avalonia.Media.Colors.White, BrightUnless);

        return new Scheme(
            name,
            colors[0], colors[1], colors[2], colors[3], colors[4], colors[5], colors[6], colors[7],
            colors[8], colors[9], colors[10], colors[11], colors[12], colors[13], colors[14], colors[15],
            colors[16], bright, light);
    }

    /// <summary>A yes or no a file may leave out, meaning no; one it gets wrong is said.</summary>
    private static bool Flag(JsonElement root, string key, ICollection<string> problems)
    {
        if (!root.TryGetProperty(key, out var value)) return false;

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();

        problems.Add($"{key} is not true or false, so it is taken as false.");
        return false;
    }

    /// <summary>A color a file may leave out, worked out from the others when it does; one it gets wrong is said.</summary>
    private static Color? Optional(JsonElement root, string? section, string key, ICollection<string> problems)
    {
        if (Hex(root, section, key) is { } color) return color;

        var holder = root;
        var present = (section is null || (root.TryGetProperty(section, out holder) && holder.ValueKind == JsonValueKind.Object))
            && holder.TryGetProperty(key, out _);

        if (present) problems.Add($"{(section is null ? key : $"{section}.{key}")} is not a #RRGGBB color, so it is worked out from the others.");

        return null;
    }

    private static Color? Hex(JsonElement root, string? section, string key)
    {
        var holder = root;

        if (section is not null && (!root.TryGetProperty(section, out holder) || holder.ValueKind != JsonValueKind.Object))
            return null;

        if (!holder.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return null;

        var text = value.GetString()!;

        return text.Length == 7 && text[0] == '#'
            && uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
                ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
                : null;
    }

    private static Color[] Colors(Scheme scheme) =>
    [
        scheme.Edge, scheme.Outline, scheme.Window, scheme.Canvas, scheme.Panel, scheme.Toolbar,
        scheme.Grid, scheme.GridMajor, scheme.Node, scheme.NodeSelected, scheme.Separator,
        scheme.Label, scheme.Value, scheme.Normalled, scheme.Muted, scheme.Inactive,
        scheme.Attention,
    ];
}
