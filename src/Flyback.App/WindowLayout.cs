using System.Text.Json;
using Flyback.Core;

namespace Flyback.App;

/// <summary>
/// How the window was left: its size, the monitor it was on, the panels and which
/// of them were open, and which view was showing (ADR-0121).
/// </summary>
/// <remarks>
/// Deliberately no position: several copies may be open at once and the platform
/// places each where it can be seen, so only the monitor is remembered. The size is
/// the last one the window had while not maximized, so leaving maximized still
/// gives a window of the size it was left at. Not load-bearing (ADR-0034): an
/// unreadable file means the default layout.
/// </remarks>
public sealed class WindowLayout
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public bool Maximized { get; set; }

    /// <summary>The client size in device-independent pixels, or 0 for the window's own default.</summary>
    public double Width { get; set; }

    /// <inheritdoc cref="Width"/>
    public double Height { get; set; }

    /// <summary>The monitor the window was on, or null when that could not be told.</summary>
    public MonitorSpot? Monitor { get; set; }

    /// <summary>The weights of the canvas column and the column beside it.</summary>
    public double CanvasWeight { get; set; } = DefaultCanvasWeight;

    /// <inheritdoc cref="CanvasWeight"/>
    public double SideWeight { get; set; } = DefaultSideWeight;

    /// <summary>The weights of the preview and the inspector under it.</summary>
    public double PreviewWeight { get; set; } = DefaultPreviewWeight;

    /// <inheritdoc cref="PreviewWeight"/>
    public double InspectorWeight { get; set; } = DefaultInspectorWeight;

    /// <summary>Pixels, and kept while the panel is closed.</summary>
    public double AssistantWidth { get; set; } = DefaultAssistantWidth;

    /// <inheritdoc cref="AssistantWidth"/>
    public double ControlsHeight { get; set; } = DefaultControlsHeight;

    public bool AssistantOpen { get; set; }

    public bool ControlsOpen { get; set; }

    /// <summary>The text view is showing rather than the canvas.</summary>
    public bool Code { get; set; }

    /// <summary>The preview and the canvas have traded places.</summary>
    public bool Swapped { get; set; }

    public const double DefaultCanvasWeight = 3, DefaultSideWeight = 1.6;

    public const double DefaultPreviewWeight = 1, DefaultInspectorWeight = 1.1;

    public const double DefaultAssistantWidth = 320, DefaultControlsHeight = 118;

    public static string File => Path.Combine(GlobalConstants.DataFolder, "layout.json");

    /// <summary>Null when there is no file or it cannot be read. Never throws.</summary>
    public static WindowLayout? Load(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;

            return JsonSerializer.Deserialize<WindowLayout>(System.IO.File.ReadAllText(path), Options)?.Sane();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// Brings every number into a range the layout can use, since the file is one
    /// somebody may have edited by hand.
    /// </summary>
    private WindowLayout Sane()
    {
        Width = Size(Width);
        Height = Size(Height);

        CanvasWeight = Weight(CanvasWeight, DefaultCanvasWeight);
        SideWeight = Weight(SideWeight, DefaultSideWeight);
        PreviewWeight = Weight(PreviewWeight, DefaultPreviewWeight);
        InspectorWeight = Weight(InspectorWeight, DefaultInspectorWeight);

        AssistantWidth = Pixels(AssistantWidth, DefaultAssistantWidth);
        ControlsHeight = Pixels(ControlsHeight, DefaultControlsHeight);

        return this;

        static double Size(double value) => double.IsFinite(value) && value > 0 ? Math.Min(value, 16384) : 0;

        static double Weight(double value, double fallback) =>
            double.IsFinite(value) && value > 0 ? Math.Clamp(value, 0.05, 100) : fallback;

        static double Pixels(double value, double fallback) =>
            double.IsFinite(value) && value > 0 ? Math.Min(value, 16384) : fallback;
    }

    /// <summary>A monitor as the platform described it: its name and where it sits in the desktop.</summary>
    public sealed class MonitorSpot
    {
        public string? Name { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }
}
