using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A saw folded and then filtered, which is the order both modules are usually
/// wanted in: make the harmonics first, take them away second.
/// </summary>
/// <remarks>
/// Two hands on the instrument and nothing else in the patch, both far below
/// audio rate: one opens the filter, the other drives the fold. Everything that
/// moves is moved by one of those two, so what is heard can be traced to a knob
/// without following a wire.
/// <para>
/// Nothing is drawn. It used to show a ring field put through a second Fold at
/// the same drive, which was honest as far as it went — folding is pure and does
/// the same arithmetic at both sinks — but the patch is named for the filter, and
/// the filter is the half that cannot be shown: it holds its integrators in the
/// one-evaluation cells ([0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)),
/// the video path has none, and it is a wire there. So the picture was showing
/// one of the two modules and standing in for the other, which is the reading
/// this preset now avoids by showing neither.
/// </para>
/// </remarks>
internal static class TimbrePreset
{
    public const string Name = "Filter sweep";

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // No clock: every oscillator here runs on the Time its 'in' is normalled
        // to, so the patch is nothing but the two hands and what they are on
        // (ADR-0050).
        var sweep = b.Add("osc.sine", 250, 860, (1, 0.12f));
        var wobble = b.Add("osc.sine", 250, 1080, (1, 0.07f));

        var cutoff = b.Add("math.remap", 470, 860, (3, 180f), (4, 5000f));
        var drive = b.Add("math.remap", 470, 1080, (3, 1f), (4, 4f));

        // 110 Hz, folded into a spectrum, then filtered back down out of it.
        var pitch = b.Add("audio.frequency", 250, 640, (0, 110f));
        var saw = b.Add("osc.saw", 470, 600, (3, 0.9f));
        var fold = b.Add(FoldModule.TypeId, 700, 600);
        var filter = b.Add(FilterModule.TypeId, 930, 640, (2, 0.75f));

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1250, 640, (NodeCatalog.OutputGainPort, 0.45f));

        b.Wire(sweep, 0, cutoff, 0)
         .Wire(wobble, 0, drive, 0)

         .Wire(pitch, 0, saw, 1)
         .Wire(saw, 0, fold, 0)
         .Wire(drive, 0, fold, 1)
         .Wire(fold, 0, filter, 0)
         .Wire(cutoff, 0, filter, 1)
         .Wire(filter, 0, output, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }
}
