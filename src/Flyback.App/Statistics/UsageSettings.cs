using System.Text.Json;
using Flyback.Core;

namespace Flyback.App.Statistics;

/// <summary>
/// Whether Flyback counts how it is used — the Usage section of the settings
/// window.
/// </summary>
/// <remarks>
/// A file of its own rather than a field on <see cref="Updates.UpdateSettings"/> or
/// <see cref="OutputSettings"/>, for the reason each of those is one: this is about
/// what Flyback says about itself, not about what it installs or what comes out of
/// it. Not load-bearing (ADR-0034): an unreadable file means the defaults, and the
/// default is on (ADR-0094).
/// </remarks>
public sealed class UsageSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Count what this run is made of and send it, anonymously and with nothing
    /// about any patch in it. Off stops the run it is switched off in.
    /// </summary>
    public bool SendUsageStatistics { get; set; } = true;

    public static string File => Path.Combine(GlobalConstants.DataFolder, "usage.json");

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static UsageSettings Load(string path)
    {
        try
        {
            return System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<UsageSettings>(System.IO.File.ReadAllText(path), Options) ?? new()
                : new UsageSettings();
        }
        catch
        {
            return new UsageSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
