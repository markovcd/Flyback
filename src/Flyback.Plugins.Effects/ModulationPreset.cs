using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// All three sweeps in the order a pedalboard would have them — flanger, phaser,
/// chorus — with the chorus last because its two outputs are what make the result
/// stereo.
/// </summary>
/// <remarks>
/// One saw goes in and nothing but movement happens to it. The three run at rates that
/// share no common factor worth the name, so the combination never quite repeats.
/// Nothing is drawn: what a flanger does is comb its input, and the comb is the part
/// with a delay line in it, which is a wire on the screen.
/// </remarks>
internal static class ModulationPreset
{
    public const string Name = "Moving parts";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // No clock: the saw's 'in' is normalled to Time (ADR-0050), so the whole
        // patch is the effects and the one thing they are applied to.
        var pitch = b.Add("audio.frequency", (0, 165f));
        var saw = b.Add("osc.saw", (3, 0.9f));

        var flanger = b.Add(FlangerModule.TypeId, (1, 0.18f), (2, 0.8f), (3, 0.45f), (4, 0.35f));
        var phaser = b.Add(PhaserModule.TypeId, (1, 0.4f), (2, 0.75f), (3, 0.5f), (4, 0.5f));
        var chorus = b.Add(ChorusModule.TypeId, (1, 0.6f), (2, 0.6f), (3, 0.5f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.6f));

        b.Wire(pitch, 0, saw, 1)
         .Wire(saw, 0, flanger, 0)
         .Wire(flanger, 0, phaser, 0)
         .Wire(phaser, 0, chorus, 0)
         .Wire(chorus, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(chorus, 1, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }
}
