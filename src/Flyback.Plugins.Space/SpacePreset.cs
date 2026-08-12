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

        var coord = b.Add("coord", 40, 120);
        var time = b.Add("time", 40, 520, (0, 1f));

        // A saw falling from 1 to 0 twice a second, used as a pluck envelope:
        // sharp attack, and the tail is what the delay and reverb are fed.
        var pluck = b.Add("osc.saw", 250, 520, (1, 2f), (3, -0.5f), (4, 0.5f));

        var pitch = b.Add("audio.frequency", 250, 740, (0, 330f));
        var tone = b.Add("osc.sine", 470, 700);
        var struck = b.Add("math.mul", 660, 660);

        var echo = b.Add("flyback.space.delay", 840, 620, (1, 0.33f), (2, 0.5f), (3, 0.45f));
        var room = b.Add("flyback.space.reverb", 1030, 660, (1, 0.7f), (2, 0.75f), (3, 0.35f));
        var speaker = b.Add(NodeCatalog.AudioOutputTypeId, 1230, 700, (2, 0.5f));

        // Eye: the same envelope as brightness, so you can see the pluck the
        // repeats are made of even though the repeats themselves are not visible.
        var rings = b.Add("pattern.rings", 250, 120, (2, 2.5f));
        var colour = b.Add("colour.hsv", 660, 160, (0, 0.55f), (1, 0.7f));
        var screen = b.Add(NodeCatalog.VideoOutputTypeId, 900, 180);

        b.Wire(time, 0, pluck, 0)
         .Wire(time, 0, tone, 0)
         .Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, struck, 0)
         .Wire(pluck, 0, struck, 1)
         .Wire(struck, 0, echo, 0)
         .Wire(echo, 0, room, 0)
         .Wire(room, 0, speaker, 0)
         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(time, 0, rings, 3)
         .Wire(rings, 0, colour, 2)
         .Wire(colour, 0, screen, 0);

        return b.Patch;
    }
}
