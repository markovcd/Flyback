using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A plucked tone through the delay into the reverb, which is the arrangement
/// both modules are usually wanted in: repeats first, then the room they happen
/// in.
/// </summary>
internal static class SpacePreset
{
    public const string Name = "Echo chamber";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // A saw falling from 1 to 0 twice a second, used as a pluck envelope:
        // sharp attack, and the tail is what the delay and reverb are fed. Its
        // 'in' takes no wire — it is a domain, normalled to Time (ADR-0050).
        var pluck = b.Add("osc.saw", (1, 2f), (3, -0.5f), (4, 0.5f));

        var pitch = b.Add("audio.frequency", (0, 330f));
        var tone = b.Add("osc.sine");
        var struck = b.Add("math.mul");

        var echo = b.Add("flyback.effects.delay", (1, 0.33f), (2, 0.5f), (3, 0.45f));
        var room = b.Add("flyback.effects.reverb", (1, 0.7f), (2, 0.75f), (3, 0.35f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, struck, 0)
         .Wire(pluck, 0, struck, 1)
         .Wire(struck, 0, echo, 0)
         .Wire(echo, 0, room, 0)
         .Wire(room, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(room, 1, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }
}
