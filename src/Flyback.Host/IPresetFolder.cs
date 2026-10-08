namespace Flyback.Host;

/// <summary>Where a window keeps the presets somebody saved.</summary>
public interface IPresetFolder
{
    /// <summary>The folder, or null to keep none.</summary>
    string? PresetFolder { get; }
}