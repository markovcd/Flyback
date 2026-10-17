using System.Text.Json;
using Flyback.Core;

namespace Flyback.Editor.Assist;

/// <summary>Whether a program on this computer may edit the open patch over MCP (ADR-0202).</summary>
/// <remarks>Off by default; an unreadable section means off (ADR-0034).</remarks>
public sealed class DrivenSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Let a program on this computer edit the open patch: the editor listens on its channel while this is on.</summary>
    public bool LetProgramsEdit { get; set; }

    /// <summary>Where these settings are kept in <see cref="SettingsFile"/>.</summary>
    public const string Section = "driven";

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    public static DrivenSettings Load(string path)
    {
        try
        {
            return SettingsFile.Read(path, Section) is { } json
                ? JsonSerializer.Deserialize<DrivenSettings>(json, Options) ?? new()
                : new DrivenSettings();
        }
        catch
        {
            return new DrivenSettings();
        }
    }

    /// <summary>Throws if it cannot write, so the caller can say so.</summary>
    public void Save(string path) => SettingsFile.Write(path, Section, JsonSerializer.Serialize(this, Options));
}
