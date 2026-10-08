using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Drawings;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(PathModule.TypeId, "Path")]
[assembly: FlybackModule(RotateModule.TypeId, "Rotate 3D")]
[assembly: FlybackModule(TranslateModule.TypeId, "Translate 3D")]
[assembly: FlybackModule(ScaleModule.TypeId, "Scale 3D")]
[assembly: FlybackModule(PerspectiveModule.TypeId, "Perspective")]

namespace Flyback.Plugins.Drawings;

/// <summary>
/// A drawing played as sound that draws it on a Beam, and the modules that turn a
/// model in 3D on its way there (ADR-0185).
/// </summary>
public sealed class DrawingsPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.drawings", "Drawings");

    /// <summary>The category all five sit under in the palette, and nowhere else.</summary>
    public const string Category = "Drawings";

    public PluginInfo Info { get; } = new(
        "flyback.drawings",
        "Drawings",
        "An SVG, an OBJ model or a PNG's outlines played as sound that draws it on a Beam, "
        + "and the modules that turn, move and project a model in 3D.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                PathModule.Definition,
                RotateModule.Definition,
                TranslateModule.Definition,
                ScaleModule.Definition,
                PerspectiveModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                WireframePreset.Name,
                WireframePreset.Build,
                "A cube's edges played as one path at a pitch, turned in perspective as it plays: a model drawn by its sound.",
                PresetKind.Interplay)
            {
                Files = PresetFiles.Embedded(typeof(WireframePreset).Assembly, WireframePreset.Name),
                Tags = ["stereo", "scope", "3d"],
            },
        ]);
    }
}
