using System.Text.Json;
using Flyback.Core;

namespace Flyback.App.Updates;

/// <summary>
/// Whether Flyback looks for a new release when it starts — the Updates section of
/// the settings window.
/// </summary>
/// <remarks>
/// A file of its own rather than a field on <see cref="OutputSettings"/>, which is
/// what comes out of the instrument; this is about the program. Not load-bearing,
/// for the reason neither of the others is (ADR-0034): an unreadable file means the
/// defaults, and the default is on (ADR-0088).
/// </remarks>
public sealed class UpdateSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Look for a release at startup, download it in the background and install it
    /// at the next start. Off also stops a release already downloaded from being
    /// installed.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    public static string File => Path.Combine(GlobalConstants.DataFolder, "update.json");

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static UpdateSettings Load(string path)
    {
        try
        {
            return System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<UpdateSettings>(System.IO.File.ReadAllText(path), Options) ?? new()
                : new UpdateSettings();
        }
        catch
        {
            return new UpdateSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
