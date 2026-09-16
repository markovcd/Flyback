using System.Text.Json;
using Flyback.Core;

namespace Flyback.App;

/// <summary>
/// What the picture is drawn at and by, and whether the processor runs the patch
/// as machine code — the Output settings in the settings window.
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

    public static string File => Path.Combine(GlobalConstants.DataFolder, "output.json");

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static OutputSettings Load(string path)
    {
        try
        {
            return System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<OutputSettings>(System.IO.File.ReadAllText(path), Options) ?? new()
                : new OutputSettings();
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
