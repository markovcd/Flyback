using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Easy;

/// <summary>
/// Acid house on the two Easy modules: a squelching bass line over a drum machine,
/// with a supersaw stab on top.
/// </summary>
/// <remarks>
/// The genre the two were built for: the drums are a drum machine's kick, clap,
/// hats and cowbell, and a saw through a resonant low pass that the envelope sweeps,
/// with a short glide between notes, is the bass line acid house was made on. An
/// accent line drives the bass's velocity, which opens its filter further on the
/// accented notes. The drums keep their own time at 124 bpm and the sequencers step
/// at the same sixteenths, so nothing is wired for timing. LFO 1 sweeps the bass's
/// filter over half a minute, and the Acid knob sets where it sweeps from. The picture
/// is rings pushed by the kick, turned round the color wheel by the filter's sweep
/// and lit by the kick, the clap and the bass.
/// </remarks>
internal static class WarehousePreset
{
    public const string Name = "Warehouse";

    private const float Bpm = 124f;

    /// <summary>Sixteenths a second at <see cref="Bpm"/>, which is what the drums count in.</summary>
    private const float Sixteenths = Bpm / 15f;

    /// <summary>One bar of the bass, in A minor: a note number, or nought for a rest.</summary>
    private static readonly int[] Line = [33, 0, 45, 33, 0, 36, 33, 43, 33, 0, 45, 40, 38, 0, 36, 31];

    /// <summary>The accented steps of the bass.</summary>
    private static readonly int[] Accents = [2, 6, 10, 12];

    /// <summary>Two bars of the stab, in eighths.</summary>
    private static readonly int[] Stabs = [0, 57, 0, 0, 0, 57, 0, 60, 0, 57, 0, 0, 0, 55, 0, 64];

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var acid = b.Patch.AddControl("Acid", 0.3f);

        // --- the drums ---------------------------------------------------------

        var kick = Drum(Kit.Kick, (DrumModule.DrivePort, 0.4f));
        var clap = Drum(Kit.Clap, (DrumModule.VelocityPort, 0.8f));
        var closed = Drum(
            Kit.ClosedHat,
            (DrumModule.VelocityPort, 0.4f), (DrumModule.SwingPort, 0.15f), (DrumModule.PanPort, -0.3f));
        DrumModule.Configure(closed, (DrumModule.SoundKey, Kit.ClosedHat), (DrumModule.RhythmKey, Rhythms.Sixteenths));
        var open = Drum(
            Kit.OpenHat,
            (DrumModule.VelocityPort, 0.45f), (DrumModule.DecayPort, 0.3f), (DrumModule.PanPort, 0.3f));
        var cowbell = Drum(Kit.Cowbell, (DrumModule.VelocityPort, 0.3f), (DrumModule.PanPort, 0.5f));
        DrumModule.Configure(cowbell, (DrumModule.SoundKey, Kit.Cowbell), (DrumModule.RhythmKey, Rhythms.Clave));

        // --- the bass line -----------------------------------------------------

        var notes = b.Add("seq.notes", (1, Sixteenths));
        StepsExtra.Set(notes, [.. Line.Select(n => new Step(n, 1f, n == 0 ? 0f : 1f))]);

        var accents = b.Add("seq.values", (1, Sixteenths), (2, 1f));
        StepsExtra.Set(accents, [.. Enumerable.Range(0, Line.Length).Select(i => new Step(Accents.Contains(i) ? 1f : 0.65f))]);

        var bass = SynthModule.Configure(
            b.Add(
                SynthModule.TypeId,
                (SynthModule.GlidePort, 0.05f),
                (SynthModule.AttackPort, -4f),
                (SynthModule.DecayPort, -0.8f),
                (SynthModule.SustainPort, 0.15f),
                (SynthModule.ReleasePort, -1.5f),
                (SynthModule.ResonancePort, 0.85f),
                (SynthModule.SweepPort, 0.75f),
                (SynthModule.Lfo1RatePort, 1f / 32f),
                (SynthModule.Lfo1DepthPort, 0.4f),
                (SynthModule.DrivePort, 0.45f)),
            (SynthModule.WaveKey, Waves.Saw),
            (SynthModule.Lfo1TargetKey, SynthModule.Filter));
        Knob(bass, SynthModule.BrightPort, acid, 0f, 0.5f);

        // --- the stab ----------------------------------------------------------

        var chords = b.Add("seq.notes", (1, Sixteenths / 2f), (2, 0.35f));
        StepsExtra.Set(chords, [.. Stabs.Select(n => new Step(n, 1f, n == 0 ? 0f : 1f))]);

        var stab = SynthModule.Configure(
            b.Add(
                SynthModule.TypeId,
                (SynthModule.VelocityPort, 0.8f),
                (SynthModule.AttackPort, -2.5f),
                (SynthModule.DecayPort, -0.9f),
                (SynthModule.SustainPort, 0f),
                (SynthModule.ReleasePort, -0.8f),
                (SynthModule.BrightPort, 0.45f),
                (SynthModule.SweepPort, 0.5f)),
            (SynthModule.WaveKey, Waves.Supersaw));

        b.Wire(notes, 0, bass, SynthModule.PitchPort)
         .Wire(notes, 1, bass, SynthModule.GatePort)
         .Wire(accents, 0, bass, SynthModule.VelocityPort)
         .Wire(chords, 0, stab, SynthModule.PitchPort)
         .Wire(chords, 1, stab, SynthModule.GatePort);

        // --- the mix -----------------------------------------------------------

        // Two Desks: the drums on the first, bussed into the second with the cowbell and the synths.
        var drums = b.Add(NodeCatalog.DeskTypeId);
        var master = b.Add(NodeCatalog.DeskTypeId, (2, 0.3f), (5, 0.75f), (8, 0.3f), (14, 0.8f));

        Channel(drums, 0, kick);
        Channel(drums, 1, clap);
        Channel(drums, 2, closed);
        Channel(drums, 3, open);
        Channel(master, 0, cowbell);
        Channel(master, 1, bass);
        Channel(master, 2, stab);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(drums, 2, master, 12)
         .Wire(drums, 3, master, 13)
         .Wire(master, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(master, 1, output, NodeCatalog.OutputRightPort);

        // --- the picture -------------------------------------------------------

        var rings = b.Add("pattern.rings", (2, 3f));
        var hue = b.Add("math.add");
        var lit = b.Add("math.add");
        var glow = b.Add("math.add");
        var color = b.Add("color.hsv", (1, 0.85f));

        b.Wire(kick, DrumModule.EnvPort, rings, 3)
         .Wire(rings, 0, hue, 0)
         .Wire(bass, SynthModule.LfoPort, hue, 1)
         .Wire(hue, 0, color, 0)
         .Wire(kick, DrumModule.EnvPort, lit, 0)
         .Wire(clap, DrumModule.EnvPort, lit, 1)
         .Wire(lit, 0, glow, 0)
         .Wire(bass, SynthModule.EnvPort, glow, 1)
         .Wire(glow, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();

        NodeInstance Drum(string sound, params (int Port, float Value)[] knobs) =>
            DrumModule.Configure(b.Add(DrumModule.TypeId, [(DrumModule.BpmPort, Bpm), .. knobs]), (DrumModule.SoundKey, sound));

        void Channel(NodeInstance desk, int channel, NodeInstance voice) =>
            b.Wire(voice, 0, desk, channel * 3).Wire(voice, 1, desk, channel * 3 + 1);
    }

    /// <summary>A socket turned by a panel knob from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static void Knob(NodeInstance node, int port, PatchControl knob, float low, float high)
    {
        var link = new ControlLink(knob.Id, low, high);

        node.InputValues[port] = link.At(knob.Value);
        ControlMap.Link(node, port, link);
    }
}
