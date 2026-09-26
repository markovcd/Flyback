namespace Flyback.Server;

/// <summary>A page of presets and how many match in all.</summary>
internal sealed record PresetPage(IReadOnlyList<StoredPreset> Items, int Total);