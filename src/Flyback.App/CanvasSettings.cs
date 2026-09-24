using System.Text.Json;
using Flyback.Core;

namespace Flyback.App;

/// <summary>
/// How the patch canvas draws what is on it — the Canvas section of the settings
/// window.
/// </summary>
/// <remarks>
/// A file of its own rather than a field on <see cref="OutputSettings"/>, for the
/// reason that one names: this is about the editor rather than about what comes
/// out of the program. Not load-bearing (ADR-0034): an unreadable file means the
/// defaults, and both defaults are what a plugin author asked for.
/// </remarks>
public sealed class CanvasSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Whether a plugin's module is drawn in the background its author gave it
    /// (ADR-0118). Off draws every module as its category, the way the engine's
    /// own are drawn.
    /// </summary>
    public bool PluginSkins { get; set; } = true;

    /// <summary>
    /// Whether an animated picture behind a module runs. Off holds it at its
    /// first frame, which is also what stops the canvas repainting on a clock.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PluginSkins"/> because they answer different
    /// complaints: one is "I do not want a plugin choosing how my patch looks",
    /// the other is "I do not want anything moving while I work".
    /// </remarks>
    public bool AnimateSkins { get; set; } = true;

    /// <summary>
    /// Whether modules are drawn compact: an input and an output share each row, and
    /// a knob's value is in its socket's tooltip rather than on the row.
    /// </summary>
    public bool CompactModules { get; set; }

    /// <summary>The text editor's font size, set by Ctrl+scroll over it.</summary>
    public double EditorFontSize { get; set; } = DefaultEditorFontSize;

    public const double DefaultEditorFontSize = 13;

    public const double MinEditorFontSize = 8;

    public const double MaxEditorFontSize = 40;

    /// <summary>How many seconds the seek bar spans, set in the box beside it.</summary>
    public double SeekLength { get; set; } = DefaultSeekLength;

    public const double DefaultSeekLength = 60;

    public const double MinSeekLength = 1;

    public const double MaxSeekLength = 24 * 60 * 60;

    public static string File => Path.Combine(GlobalConstants.DataFolder, "canvas.json");

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static CanvasSettings Load(string path)
    {
        try
        {
            var loaded = System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<CanvasSettings>(System.IO.File.ReadAllText(path), Options) ?? new()
                : new CanvasSettings();

            loaded.EditorFontSize = double.IsFinite(loaded.EditorFontSize)
                ? Math.Clamp(loaded.EditorFontSize, MinEditorFontSize, MaxEditorFontSize)
                : DefaultEditorFontSize;

            loaded.SeekLength = double.IsFinite(loaded.SeekLength)
                ? Math.Clamp(loaded.SeekLength, MinSeekLength, MaxSeekLength)
                : DefaultSeekLength;

            return loaded;
        }
        catch
        {
            return new CanvasSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
