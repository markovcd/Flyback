using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Render;

namespace Flyback.Cli;

/// <summary>
/// What <c>render</c> writes when a flag is left out: the editor's preview size and
/// its Settings → Recording, read from the <c>output.json</c> the editor saves.
/// </summary>
/// <remarks>
/// Read here rather than through the editor's own settings class, which would bring
/// Avalonia with it. Only the fields a render uses are read, each brought into the
/// range the editor keeps it in; a missing or unreadable file is the editor's defaults.
/// </remarks>
internal sealed record ExportDefaults(
    int Width = 960,
    int Height = 540,
    double Fps = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality,
    ClipFormat? Video = null,
    ClipFormat? Sound = null,
    string? Ffmpeg = null)
{
    public static string File => Path.Combine(GlobalConstants.DataFolder, "output.json");

    /// <summary>Never throws: a settings file is not worth a failed render.</summary>
    public static ExportDefaults Load(string path)
    {
        var defaults = new ExportDefaults();

        try
        {
            if (!System.IO.File.Exists(path)) return defaults;

            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = document.RootElement;

            int width = Int(root, "width", defaults.Width), height = Int(root, "height", defaults.Height);
            if (width <= 0 || height <= 0 || (long)width * height > SynthRenderer.MostPixels)
                (width, height) = (defaults.Width, defaults.Height);

            return new ExportDefaults(
                width,
                height,
                Math.Clamp(Number(root, "frameRate", defaults.Fps), 1d, 120d),
                Math.Clamp(Int(root, "jpegQuality", defaults.Quality), 1, 100),
                Format(root, "videoFormat", picture: true),
                Format(root, "soundFormat", picture: false),
                Text(root, "ffmpegPath") is { Length: > 0 } ffmpeg ? ffmpeg : null);
        }
        catch
        {
            return defaults;
        }
    }

    /// <summary>
    /// The format a file name asks for, preferring the saved one where its extension
    /// matches, as a take does: <c>.mp4</c> is H.265 to someone who chose H.265.
    /// </summary>
    public string? FormatFor(string path)
    {
        var extension = Path.GetExtension(path);

        foreach (var saved in new[] { Video, Sound })
            if (saved is not null && extension.Equals(saved.Extension, StringComparison.OrdinalIgnoreCase))
                return saved.Id;

        return null;
    }

    /// <summary>The <c>--settings</c> named on a command line, found before the command is built so its help shows the defaults it read.</summary>
    public static string? PathIn(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--settings") return i + 1 < args.Length ? args[i + 1] : null;

            if (args[i].StartsWith("--settings=", StringComparison.Ordinal)) return args[i]["--settings=".Length..];
        }

        return null;
    }

    private static int Int(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static double Number(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : fallback;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static ClipFormat? Format(JsonElement root, string name, bool picture) =>
        ClipFormats.ById(Text(root, name)) is { } format && format.HasPicture == picture ? format : null;
}
