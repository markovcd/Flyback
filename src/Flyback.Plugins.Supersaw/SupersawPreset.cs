using Flyback.Core.Graph;

namespace Flyback.Plugins.Supersaw;

/// <summary>
/// A patch to start from, wired the way the module is meant to be driven. It is
/// half the point of shipping the plugin: <c>freq</c> is in cycles per unit of
/// <c>in</c> like every other oscillator here, so the pitch comes from a
/// Frequency module — reach for the knob instead and you get a one-hertz saw,
/// which is a click rather than a note.
/// </summary>
internal static class SupersawPreset
{
    public const string Name = "Supersaw";

    /// <summary>
    /// One slow sweep drives the detune of both sinks, so the sound widening and
    /// the bands on screen drifting apart are visibly the same signal — the
    /// house pattern the Drone preset established.
    /// </summary>
    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 120);
        var time = b.Add("time", 40, 460, (0, 1f));

        // 0..1 over about sixteen seconds, from amp and bias at a half.
        var sweep = b.Add("osc.sine", 250, 380, (1, 0.06f), (3, 0.5f), (4, 0.5f));

        // Ear: 110 Hz, both outputs to their own channel so it is actually wide.
        var pitch = b.Add("audio.frequency", 250, 700, (0, 110f));
        var voice = b.Add(SupersawModule.TypeId, 480, 600, (3, 0.9f));
        var speaker = b.Add(NodeCatalog.AudioOutputTypeId, 780, 640, (2, 0.5f));

        // Eye: the same module across x, its output mapped to 0..1 by amp and
        // bias. It drives brightness rather than hue — the voices beating in and
        // out of step is the thing worth seeing, and reading it as hue would
        // wrap it through the whole colour wheel and bury it. The sweep takes
        // the hue instead, so the picture drifts in colour as the sound widens.
        var bands = b.Add(SupersawModule.TypeId, 480, 90, (1, 2f), (3, 1f), (5, 0.5f), (6, 0.5f));
        var colour = b.Add("colour.hsv", 800, 140, (1, 0.8f));
        var screen = b.Add(NodeCatalog.VideoOutputTypeId, 1030, 160);

        b.Wire(time, 0, sweep, 0)
         .Wire(time, 0, voice, 0)
         .Wire(pitch, 0, voice, 1)
         .Wire(sweep, 0, voice, 2)
         .Wire(voice, 0, speaker, 0)
         .Wire(voice, 1, speaker, 1)
         .Wire(coord, 0, bands, 0)
         .Wire(sweep, 0, bands, 2)
         .Wire(sweep, 0, colour, 0)
         .Wire(bands, 0, colour, 2)
         .Wire(colour, 0, screen, 0);

        return b.Patch;
    }
}
