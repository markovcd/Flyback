using Flyback.Core.Graph;

namespace Flyback.Plugins.Modulation;

/// <summary>
/// Everything the two new plugins added, in one signal chain and in the order a
/// rack would have it: make the harmonics, take them away, then move what is
/// left. A tune runs underneath so that there is something for all of it to
/// happen to.
/// </summary>
/// <remarks>
/// It is the one preset here that reaches across a plugin boundary — the shaping
/// modules are in <c>Flyback.Plugins.Timbre</c> and the moving ones are here —
/// which is allowed and is not free. A preset is handed the catalogue when it is
/// picked rather than when it is registered, so this is where a missing plugin
/// shows up; the check below is only to make it say which one, rather than
/// naming a module id nobody asked about.
/// <para>
/// Both ship in the box, so in practice the check never fires. It exists for the
/// case the plugin folder is a folder like any other and somebody has been in it.
/// </para>
/// <para>
/// The picture takes its slow movement from a sine of its own rather than from a
/// chorus's <c>lfo</c>, which is the one thing here done differently from
/// <see cref="ModulationPreset"/> and is done differently on purpose. A module is
/// resolved whole: reaching for one of its outputs compiles everything upstream
/// of <em>all</em> its inputs, because an emit function is one function and the
/// compiler cannot know that <c>lfo</c> ignores <c>in</c>. At the end of a chain
/// six effects long that costs the video program the entire chain — 314 ops
/// against the 141 of the next most expensive preset that ships. A separate sine
/// costs eleven and moves at a rate of its own besides.
/// </para>
/// </remarks>
internal static class WholeRackPreset
{
    public const string Name = "Whole rack";

    private const string Timbre = "flyback.timbre";

    private const string Fold = "flyback.timbre.fold";
    private const string Drive = "flyback.timbre.drive";
    private const string Filter = "flyback.timbre.filter";

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Timbre))
            throw new InvalidOperationException(
                $"it needs the Filter and fold plugin ({Timbre}), which is not installed.");

        var b = new PatchBuilder(modules);

        // Here for the Rings' 'offset', which is the one socket in the patch
        // that has to be told to move: every 'in' is normalled to Time already,
        // and so are the Rings' own x and y to Coordinates (ADR-0050).
        var time = b.Add("time", 40, 700);

        // The tune, four steps a second, with a gate that swells rather than
        // switches. Everything else in the patch is timed off these three
        // outputs and nothing anywhere is timed off anything else.
        var riff = b.Add("seq.notes", 250, 700, (1, 4f), (2, 0.55f), (3, 0.08f));
        var note = b.Add("audio.note", 470, 860);
        var tone = b.Add("osc.saw", 700, 820, (3, 0.9f));

        // One slow sine drives both folders, so the harmonics the ear gains are
        // the bands the eye gains, at the same moment.
        var breath = b.Add("osc.sine", 250, 1120, (1, 0.09f));
        var reach = b.Add("math.remap", 470, 1120, (3, 1f), (4, 3.2f));

        // A second one, for the picture alone, at a rate that shares no factor
        // with the first. Moving parts takes this from a chorus's own sweep and
        // this one deliberately does not — see the remarks above.
        var drift = b.Add("osc.sine", 250, 500, (1, 0.13f));

        // Shaping: fold to put harmonics in, drive to round what that did, and
        // the gate to make it a note rather than a drone.
        var fold = b.Add(Fold, 940, 820);
        var drive = b.Add(Drive, 1150, 820, (1, 3f));
        var struck = b.Add("math.mul", 1360, 860);

        // The gesture the whole plugin exists for: the gate opens the filter as
        // well as the note, so every step is heard arriving rather than merely
        // starting. One Remap is the entire envelope.
        var cutoff = b.Add("math.remap", 1360, 1080, (3, 320f), (4, 4200f));
        var filter = b.Add(Filter, 1570, 900, (2, 0.6f));

        // Movement, in the order a pedalboard would have it, and the chorus last
        // because its two outputs are what make the result stereo.
        var phaser = b.Add(PhaserModule.TypeId, 1790, 880, (1, 0.35f), (2, 0.8f), (3, 0.5f), (4, 0.45f));
        var flanger = b.Add(FlangerModule.TypeId, 2010, 900, (1, 0.12f), (2, 0.7f), (3, 0.4f), (4, 0.3f));
        var chorus = b.Add(ChorusModule.TypeId, 2230, 920, (1, 0.7f), (2, 0.55f), (3, 0.5f));

        // Eye: rings travelling outward, as many of them as the step the tune has
        // reached, folded by the same sine that folds the tone. Few of them
        // before the fold, because the fold is about to make more — a ring count
        // chosen for how it looks unfolded looks like a test card folded.
        var rings = b.Add("pattern.rings", 700, 140);
        var count = b.Add("math.remap", 470, 300, (3, 0.8f), (4, 2.6f));
        var bands = b.Add(Fold, 940, 140);

        // The bands are light rather than color. Hue tracking them would change
        // as fast as they do, which reads as a moiré rather than as an image;
        // hue drifts instead, and the beat arrives as brightness on top of the
        // banding rather than beside it.
        var lit = b.Add("math.remap", 1150, 140, (3, 0.08f), (4, 1f));
        var pulse = b.Add("math.remap", 1150, 320, (1, 0f), (3, 0.35f), (4, 1f));
        var value = b.Add("math.mul", 1360, 220);
        var hue = b.Add("math.remap", 1150, 500, (3, 0.5f), (4, 0.95f));
        var color = b.Add("color.hsv", 1570, 240, (1, 0.85f));

        var output = b.Add(
            NodeCatalog.OutputTypeId, 2470, 560, (NodeCatalog.OutputGainPort, 0.45f));

        b.Wire(breath, 0, reach, 0)

         .Wire(riff, 0, note, 0)
         .Wire(note, 0, tone, 1)
         .Wire(tone, 0, fold, 0)
         .Wire(reach, 0, fold, 1)
         .Wire(fold, 0, drive, 0)
         .Wire(drive, 0, struck, 0)
         .Wire(riff, 1, struck, 1)
         .Wire(struck, 0, filter, 0)
         .Wire(riff, 1, cutoff, 0)
         .Wire(cutoff, 0, filter, 1)

         .Wire(filter, 0, phaser, 0)
         .Wire(phaser, 0, flanger, 0)
         .Wire(flanger, 0, chorus, 0)
         .Wire(chorus, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(chorus, 1, output, NodeCatalog.OutputRightPort)

         .Wire(riff, 2, count, 0)
         .Wire(count, 0, rings, 2)
         .Wire(time, 0, rings, 3)
         .Wire(rings, 0, bands, 0)
         .Wire(reach, 0, bands, 1)
         .Wire(bands, 0, lit, 0)
         .Wire(riff, 1, pulse, 0)
         .Wire(lit, 0, value, 0)
         .Wire(pulse, 0, value, 1)
         .Wire(drift, 0, hue, 0)
         .Wire(hue, 0, color, 0)
         .Wire(value, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }
}
