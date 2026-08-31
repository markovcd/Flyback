using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A plucked tone through the delay into the reverb, which is the arrangement
/// both modules are usually wanted in: repeats first, then the room they happen
/// in.
/// </summary>
/// <remarks>
/// Nothing is drawn, and the black screen is the statement rather than an
/// omission. Both modules carry a delay line, a delay line is a memory
/// ([0027](0027-delay-lines-give-the-audio-path-a-memory.md)), and the video path
/// has none — so on the screen this whole patch is a wire and there is nothing of
/// it to show. A picture of the input is not a picture of the effect, and
/// lighting a pattern off the pluck envelope would be exactly that.
/// <para>
/// Both of the room's outputs are used, which is the one thing this preset exists
/// to show that a knob cannot: the same tail smeared two ways is what puts the
/// repeats around the listener rather than in front of them. Wire only 'left' and
/// the Output's normalled 'right' hands the same signal to both ears, and the
/// room collapses to a point.
/// </para>
/// </remarks>
internal static class SpacePreset
{
    public const string Name = "Echo chamber";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // A saw falling from 1 to 0 twice a second, used as a pluck envelope:
        // sharp attack, and the tail is what the delay and reverb are fed. Its
        // 'in' takes no wire — it is a domain, normalled to Time (ADR-0050).
        var pluck = b.Add("osc.saw", 250, 520, (1, 2f), (3, -0.5f), (4, 0.5f));

        var pitch = b.Add("audio.frequency", 250, 740, (0, 330f));
        var tone = b.Add("osc.sine", 470, 700);
        var struck = b.Add("math.mul", 660, 660);

        var echo = b.Add("flyback.effects.delay", 840, 620, (1, 0.33f), (2, 0.5f), (3, 0.45f));
        var room = b.Add("flyback.effects.reverb", 1030, 660, (1, 0.7f), (2, 0.75f), (3, 0.35f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1230, 620, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, struck, 0)
         .Wire(pluck, 0, struck, 1)
         .Wire(struck, 0, echo, 0)
         .Wire(echo, 0, room, 0)
         .Wire(room, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(room, 1, output, NodeCatalog.OutputRightPort);

        return b.Patch;
    }
}
