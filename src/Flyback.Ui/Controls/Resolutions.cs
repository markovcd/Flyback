using Avalonia;

namespace Flyback.App.Controls;

/// <summary>The sizes a picture is offered at, by the name the settings and the command line share.</summary>
public static class Resolutions
{
    public static readonly (string Label, PixelSize Size)[] All =
    [
        ("320 x 180", new PixelSize(320, 180)),
        ("480 x 270", new PixelSize(480, 270)),
        ("640 x 360", new PixelSize(640, 360)),
        ("960 x 540", new PixelSize(960, 540)),
        ("1280 x 720", new PixelSize(1280, 720)),
        ("1920 x 1080", new PixelSize(1920, 1080)),
        ("2560 x 1440", new PixelSize(2560, 1440)),
        ("3840 x 2160", new PixelSize(3840, 2160)),

        // Not 16:9 — the picture and the live sound's aspect both follow
        // whichever of these is picked, ADR-0083.
        ("1024 x 768", new PixelSize(1024, 768)),   // 4:3
        ("1080 x 1080", new PixelSize(1080, 1080)), // 1:1, square
        ("1080 x 1920", new PixelSize(1080, 1920)), // 9:16, portrait
        ("2560 x 1080", new PixelSize(2560, 1080)), // 21:9, ultrawide
    ];

    /// <summary>960 x 540: enough to judge a patch by, cheap enough to keep up.</summary>
    public const int Default = 3;

    /// <summary>The short names a command line may use, each a row of <see cref="All"/>.</summary>
    public static readonly IReadOnlyDictionary<string, int> Names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["180p"] = 0,
        ["270p"] = 1,
        ["360p"] = 2,
        ["540p"] = 3,
        ["720p"] = 4,
        ["1080p"] = 5,
        ["1440p"] = 6,
        ["4k"] = 7,
        ["2160p"] = 7,
        ["4:3"] = 8,
        ["square"] = 9,
        ["portrait"] = 10,
        ["ultrawide"] = 11,
    };

    /// <summary>The size a short name or a row's own label stands for, or null when it is neither.</summary>
    public static PixelSize? Named(string text)
    {
        text = text.Trim();

        if (Names.TryGetValue(text, out var row)) return All[row].Size;

        foreach (var (label, size) in All)
            if (string.Equals(label, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(label.Replace(" ", "", StringComparison.Ordinal), text, StringComparison.OrdinalIgnoreCase))
                return size;

        return null;
    }
}
