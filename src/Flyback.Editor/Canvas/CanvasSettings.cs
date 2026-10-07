using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core;
using Flyback.Engine.Measure;
using Flyback.Ui;

namespace Flyback.Editor.Canvas;

/// <summary>
/// How the patch canvas draws what is on it — the Canvas section of the settings
/// window.
/// </summary>
/// <remarks>
/// A section of its own rather than fields on <see cref="OutputSettings"/>: this is
/// about the editor rather than about what comes out of the program. Not
/// load-bearing (ADR-0034): an unreadable section means the defaults, and both
/// defaults are what a plugin author asked for.
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

    /// <summary>
    /// Whether the left button drags empty canvas to pan and the right draws the rubber
    /// band, for a mouse with no middle button to speak of (ADR-0182).
    /// </summary>
    public bool DragToPan { get; set; }

    /// <summary>The text editor's font size, set by Ctrl+scroll over it.</summary>
    public double EditorFontSize { get; set; } = DefaultEditorFontSize;

    public const double DefaultEditorFontSize = 13;

    public const double MinEditorFontSize = 8;

    public const double MaxEditorFontSize = 40;

    /// <summary>Whether the patch comes round to the start of the seek bar when it reaches its end.</summary>
    public bool SeekLoop { get; set; }

    /// <summary>How long Measure runs the patch for, in seconds.</summary>
    public double MeasureSeconds { get; set; } = MeasureOptions.DefaultSeconds;

    /// <summary>The windows Measure offers, shortest first.</summary>
    public static IReadOnlyList<double> MeasureWindows { get; } = [1, 2, 4, 8, 15, 30, 60];

    /// <summary>The grid Measure draws the picture on.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MeasurePictureSize MeasurePicture { get; set; } = MeasurePictureSize.Small;

    /// <summary>The grid a picture size is measured on, across and down.</summary>
    public static (int Columns, int Rows) MeasureGrid(MeasurePictureSize size) => size switch
    {
        MeasurePictureSize.Small => (32, 18),
        MeasurePictureSize.Medium => (64, 36),
        _ => (128, 72),
    };

    /// <summary>Where these settings are kept in <see cref="SettingsFile"/>.</summary>
    public const string Section = "canvas";

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static CanvasSettings Load(string path)
    {
        try
        {
            var loaded = SettingsFile.Read(path, Section) is { } json
                ? JsonSerializer.Deserialize<CanvasSettings>(json, Options) ?? new()
                : new CanvasSettings();

            loaded.EditorFontSize = double.IsFinite(loaded.EditorFontSize)
                ? Math.Clamp(loaded.EditorFontSize, MinEditorFontSize, MaxEditorFontSize)
                : DefaultEditorFontSize;

            if (!MeasureWindows.Contains(loaded.MeasureSeconds)) loaded.MeasureSeconds = MeasureOptions.DefaultSeconds;

            if (!Enum.IsDefined(loaded.MeasurePicture)) loaded.MeasurePicture = MeasurePictureSize.Small;

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
        SettingsFile.Write(path, Section, JsonSerializer.Serialize(this, Options));
    }
}
