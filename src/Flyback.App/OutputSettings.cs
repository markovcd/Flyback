using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;

namespace Flyback.App;

/// <summary>
/// What comes out of the program and how: the picture, a recorded take, and the
/// speakers — the Graphics, Recording and Sound sections of the settings window.
/// </summary>
/// <remarks>
/// Properties of the machine rather than of the instrument, which is why none of it
/// is saved with a patch (ADR-0037) and all of it is saved here, beside
/// <c>assistant.json</c>. Nothing here is load-bearing, for the same reason that
/// file is not (ADR-0034): an unreadable file means the defaults.
/// </remarks>
public sealed class OutputSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The preview's size in pixels. Kept as the numbers rather than as a row of
    /// the size list, so that a list with a row added or taken away still reads a
    /// file written against the old one — a size it no longer offers is the default.
    /// </summary>
    public int Width { get; set; } = 960;

    /// <inheritdoc cref="Width"/>
    public int Height { get; set; } = 540;

    /// <summary>Whether the picture is drawn by a shader. Asked for, not promised: a machine with no usable GPU draws on the processor whatever this says.</summary>
    public bool Gpu { get; set; } = true;

    /// <summary>Whether the processor runs the sound, and a picture it draws, as IL rather than interpreting it (ADR-0076).</summary>
    public bool Compiled { get; set; } = true;

    /// <summary>Frames a second in a recorded take — the Recording section.</summary>
    public double FrameRate { get; set; } = MovieRenderer.DefaultFrameRate;

    /// <summary>
    /// Frames a second the preview redraws at, or 0 to draw as fast as the
    /// renderer allows — the Graphics section. Independent of
    /// <see cref="FrameRate"/>: what is on screen and what a take writes are
    /// two different things, and a take reads whatever the preview last drew
    /// regardless of this.
    /// </summary>
    public double PreviewFrameRate { get; set; }

    /// <summary>How a recorded take's frames are compressed, from 1 to 100 — the Recording section.</summary>
    public int JpegQuality { get; set; } = JpegWriter.DefaultQuality;

    /// <summary>
    /// How far behind the patch the speakers may run, in milliseconds — the Sound
    /// section. Asked of the device when it is opened, which is once a launch.
    /// </summary>
    public int LatencyMilliseconds { get; set; } = AudioFormat.Default.LatencyMilliseconds;

    public const int LowestQuality = 1, HighestQuality = 100;

    public const double SlowestFrameRate = 1, FastestFrameRate = 120;

    public const int ShortestLatency = 5, LongestLatency = 500;

    public static string File => Path.Combine(GlobalConstants.DataFolder, "output.json");

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static OutputSettings Load(string path)
    {
        try
        {
            var settings = System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<OutputSettings>(System.IO.File.ReadAllText(path), Options) ?? new()
                : new OutputSettings();

            // Brought into range rather than refused, since the file is one
            // somebody may have edited by hand: a frame rate of nought or a
            // latency of an hour would each break something far from here.
            settings.FrameRate = Math.Clamp(settings.FrameRate, SlowestFrameRate, FastestFrameRate);

            // Nought is a real choice here — uncapped — rather than the "nobody
            // set this" that FrameRate above takes it for; only a value someone
            // actually gave is brought into range.
            settings.PreviewFrameRate = settings.PreviewFrameRate <= 0
                ? 0
                : Math.Clamp(settings.PreviewFrameRate, SlowestFrameRate, FastestFrameRate);

            settings.JpegQuality = Math.Clamp(settings.JpegQuality, LowestQuality, HighestQuality);
            settings.LatencyMilliseconds = Math.Clamp(settings.LatencyMilliseconds, ShortestLatency, LongestLatency);

            return settings;
        }
        catch
        {
            return new OutputSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
