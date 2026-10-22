using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Speech;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: FlybackModule(SpeakModule.TypeId, "Speak")]
[assembly: FlybackModule(MouthModule.TypeId, "Mouth")]

namespace Flyback.Plugins.Speech;

/// <summary>
/// A voice from pure synthesis and a mouth to go with it: Speak says or sings the
/// words typed into it, and Mouth draws the lips it says them with.
/// </summary>
public sealed class SpeechPlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.speech", "Speech");

    public PluginInfo Info { get; } = new(
        "flyback.speech",
        "Speech",
        "Speak, a voice made of a buzz, a breath and filters that says or sings the words typed "
        + "into it, and Mouth, lips that move with it.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(Provider, [SpeakModule.Definition, MouthModule.Definition]);

        registry.AddPresets(
        [
            new PatchPreset(
                "Daisy Bell",
                PresetText.Embedded(typeof(SpeechPlugin).Assembly, "DaisyBell.fbks"),
                "The song a computer first sang, sung again by a voice made of filters over a waltz, "
                + "with its mouth and the words on the screen.",
                PresetKind.Interplay)
            {
                Tags = ["song", "voice", "singing", "waltz"],
            },
        ]);
    }
}
