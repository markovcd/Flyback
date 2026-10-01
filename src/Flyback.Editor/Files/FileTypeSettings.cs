using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core;

namespace Flyback.App.Files;

/// <summary>The Files section of the settings window (ADR-0127).</summary>
/// <remarks>
/// Kept even where the operating system holds the association as well, because
/// macOS always hands the file to the editor and the editor reads this to pass it on.
/// </remarks>
public sealed class FileTypeSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<FileOpener>(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>None by default: nothing is registered until somebody asks.</summary>
    public FileOpener Opener { get; set; }

    /// <summary>Where these settings are kept in <see cref="SettingsFile"/>.</summary>
    public const string Section = "fileTypes";

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static FileTypeSettings Load(string path)
    {
        try
        {
            return SettingsFile.Read(path, Section) is { } json
                ? JsonSerializer.Deserialize<FileTypeSettings>(json, Options) ?? new()
                : new FileTypeSettings();
        }
        catch
        {
            return new FileTypeSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path)
    {
        SettingsFile.Write(path, Section, JsonSerializer.Serialize(this, Options));
    }
}
