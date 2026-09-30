namespace Flyback.Core.Graph;

/// <summary>
/// The order presets are listed in everywhere: by kind, then by name, whatever
/// order their plugins registered them in.
/// </summary>
internal static class PresetOrder
{
    public static IEnumerable<PatchPreset> Of(IEnumerable<PatchPreset> presets) =>
        presets.OrderBy(p => p.Kind).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
}
