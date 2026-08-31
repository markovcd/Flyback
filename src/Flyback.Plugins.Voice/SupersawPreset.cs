using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A patch to start from, wired the way the module is meant to be driven. It is
/// half the point of shipping the module: <c>freq</c> is in cycles per unit of
/// <c>in</c> like every other oscillator here, so the pitch comes from a
/// Frequency module — reach for the knob instead and you get a one-hertz saw,
/// which is a click rather than a note.
/// </summary>
/// <remarks>
/// One slow sweep on the detune, which is the only knob worth watching: at
/// nothing it is one saw, and opening it walks through the whole of what the
/// module is for. Both outputs go to their own channel, so the width is real
/// rather than a copy of one signal sent to two ears.
/// <para>
/// Nothing is drawn: seven voices beating against each other is a thing for
/// the ear, and a still frame of it is only a stripe pattern that happens to
/// share a knob.
/// </para>
/// </remarks>
internal static class SupersawPreset
{
    public const string Name = "Supersaw";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // 0..1 over about sixteen seconds, from amp and bias at a half. Its 'in'
        // takes no wire: it is a domain, normalled to Time (ADR-0050).
        var sweep = b.Add("osc.sine", 250, 380, (1, 0.06f), (3, 0.5f), (4, 0.5f));

        // 110 Hz, both outputs to their own channel so it is actually wide.
        var pitch = b.Add("audio.frequency", 250, 700, (0, 110f));
        var voice = b.Add(SupersawModule.TypeId, 480, 600, (3, 0.9f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1030, 600, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(pitch, 0, voice, 1)
         .Wire(sweep, 0, voice, 2)
         .Wire(voice, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(voice, 1, output, NodeCatalog.OutputRightPort);

        return b.Patch;
    }
}
