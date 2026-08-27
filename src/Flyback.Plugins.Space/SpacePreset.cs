using Flyback.Core.Graph;

namespace Flyback.Plugins.Space;

/// <summary>
/// A plucked tone through the delay into the reverb, which is the arrangement
/// both modules are usually wanted in: repeats first, then the room they happen
/// in. The picture shows the envelope, because the effects themselves have
/// nothing to show — they are audio-only.
/// </summary>
internal static class SpacePreset
{
    public const string Name = "Echo chamber";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Here for the Rings' 'offset' alone: every 'in' in the patch is
        // normalled to Time and the Rings' x and y to Coordinates (ADR-0050),
        // so the only socket left that has to be told to move is that one.
        var time = b.Add("time", 40, 520);

        // A saw falling from 1 to 0 twice a second, used as a pluck envelope:
        // sharp attack, and the tail is what the delay and reverb are fed.
        var pluck = b.Add("osc.saw", 250, 520, (1, 2f), (3, -0.5f), (4, 0.5f));

        var pitch = b.Add("audio.frequency", 250, 740, (0, 330f));
        var tone = b.Add("osc.sine", 470, 700);
        var struck = b.Add("math.mul", 660, 660);

        var echo = b.Add("flyback.space.delay", 840, 620, (1, 0.33f), (2, 0.5f), (3, 0.45f));
        var room = b.Add("flyback.space.reverb", 1030, 660, (1, 0.7f), (2, 0.75f), (3, 0.35f));

        // Eye: the same envelope as brightness, so you can see the pluck the
        // repeats are made of even though the repeats themselves are not visible.
        var rings = b.Add("pattern.rings", 250, 120, (2, 2.5f));
        var color = b.Add("color.hsv", 660, 160, (0, 0.55f), (1, 0.7f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1230, 420, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, struck, 0)
         .Wire(pluck, 0, struck, 1)
         .Wire(struck, 0, echo, 0)
         .Wire(echo, 0, room, 0)
         // Both of the room's outputs, which is the one thing this preset exists
         // to show that a knob cannot: the same tail smeared two ways is what
         // puts the repeats around the listener rather than in front of them.
         .Wire(room, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(room, 1, output, NodeCatalog.OutputRightPort)
         .Wire(time, 0, rings, 3)
         .Wire(rings, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }
}
