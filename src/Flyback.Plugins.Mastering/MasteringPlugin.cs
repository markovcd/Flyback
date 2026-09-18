using Flyback.Core.Graph;

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
        "An EQ, stereo width, a crossover, a compressor, a lookahead limiter and a loudness "
        + "meter, for the end of a patch.");

    public void Register(IPluginRegistry registry) =>
        registry.AddModules(
            Provider,
            [
                EqModule.Definition,
                WidthModule.Definition,
                CrossoverModule.Definition,
                CompressorModule.Definition,
                LimiterModule.Definition,
                LoudnessModule.Definition,
            ]);
}
