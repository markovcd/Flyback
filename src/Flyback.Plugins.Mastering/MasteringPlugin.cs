using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Mastering;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(EqModule.TypeId, "EQ")]
[assembly: FlybackModule(WidthModule.TypeId, "Width")]
[assembly: FlybackModule(CrossoverModule.TypeId, "Crossover")]
[assembly: FlybackModule(CompressorModule.TypeId, "Compressor")]
[assembly: FlybackModule(LimiterModule.TypeId, "Limiter")]
[assembly: FlybackModule(MaximizerModule.TypeId, "Maximizer")]
[assembly: FlybackModule(LoudnessModule.TypeId, "Loudness")]

namespace Flyback.Plugins.Mastering;

/// <summary>
/// What goes between the last Desk and the Output: tone, width, dynamics, and a
/// meter that reads loudness the way a streaming service does.
/// </summary>
public sealed class MasteringPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.mastering", "Mastering");

    public PluginInfo Info { get; } = new(
        "flyback.mastering",
        "Mastering",
        "An EQ, stereo width, a crossover, a compressor, a lookahead limiter, a one-knob "
        + "maximizer and a loudness meter, for the end of a patch.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(
            Provider,
            [
                EqModule.Definition,
                WidthModule.Definition,
                CrossoverModule.Definition,
                CompressorModule.Definition,
                LimiterModule.Definition,
                MaximizerModule.Definition,
                LoudnessModule.Definition,
            ]);

        registry.AddPresets(
        [
            new PatchPreset(
                BeforeAndAfterPreset.Name,
                BeforeAndAfterPreset.Build,
                "A small mix with an EQ, a Compressor and a Maximizer after it, switched out and in "
                + "every four bars."),
        ]);
    }
}
