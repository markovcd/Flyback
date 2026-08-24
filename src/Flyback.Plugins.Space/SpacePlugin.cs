using Flyback.Core.Graph;

namespace Flyback.Plugins.Space;

/// <summary>
/// The two effects that need the program to remember something: a delay, and the
/// bank of delays that makes a reverb. They ship together because they are the
/// same primitive twice, and because a plugin is the natural unit for "the parts
/// of the synth that have a past".
/// </summary>
/// <remarks>
/// Both are audio-only. On the video path there is no delay state — rows render
/// in parallel and a shared line has no per-pixel meaning — so they pass their
/// input straight through and a patch built for the speakers still shows a
/// picture. For a picture with a past, use the built-in Feedback module.
/// </remarks>
public sealed class SpacePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.space", "Delay and reverb");

    public PluginInfo Info { get; } = new(
        "flyback.space",
        "Delay and reverb",
        "Time-based effects: a feedback delay and a Schroeder reverb.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(Provider, [DelayModule.Definition, ReverbModule.Definition]);

        // Two: one for what the delay and the reverb do on their own, and one
        // that is a whole generative patch played into them — which is the thing
        // both modules are really for, and the one preset in the box with no
        // clock anywhere in it.
        registry.AddPresets(
        [
            new PatchPreset(SpacePreset.Name, SpacePreset.Build),
            new PatchPreset(SlowWeatherPreset.Name, SlowWeatherPreset.Build),
        ]);
    }
}
