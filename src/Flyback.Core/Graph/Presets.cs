namespace Flyback.Core.Graph;

/// <summary>
/// What kind of thing a preset is, which is what the picker groups by.
/// </summary>
public enum PresetKind
{
    /// <summary>
    /// A patch with its owner's part still to do: the Output alone, or a player
    /// with no file chosen, which draws and plays nothing until one is.
    /// </summary>
    /// <remarks>
    /// First, though it is the least of them: somebody who means to build their
    /// own patch should not have to read past thirty that somebody else built.
    /// The players are here rather than among the ideas because a preset that
    /// opens silent and black reads as a fault anywhere else in the list, and
    /// because the window does not open on one of these unless it is told to.
    /// </remarks>
    Blank,

    /// <summary>
    /// One idea, at one sink: a patch about sound has no picture in it, and one
    /// about picture makes no sound.
    /// </summary>
    Idea,

    /// <summary>
    /// A patch about the two sinks meeting, where taking either half away would
    /// leave no point standing.
    /// </summary>
    Interplay,

    /// <summary>
    /// What one patch can be rather than what one module does. Exempt from the
    /// one-idea rule.
    /// </summary>
    Showcase,
}

/// <summary>
/// A patch to start from, and how it is offered. Built on demand, because a
/// preset from a plugin needs that plugin's modules in the catalogue.
/// </summary>
/// <param name="Description">
/// One line saying what the patch is for, written into the patch it builds
/// where that patch does not say already.
/// </param>
public sealed record PatchPreset(
    string Name,
    Func<ModuleCatalog, Patch> Build,
    string Description = "",
    PresetKind Kind = PresetKind.Idea)
{
    private readonly Func<ModuleCatalog, Patch> build = Build;

    /// <summary>Builds the patch, carrying <see cref="Description"/> unless it has one of its own.</summary>
    public Func<ModuleCatalog, Patch> Build
    {
        get => modules =>
        {
            var patch = build(modules);

            if (patch.Description is null) patch.Describe(Description);
            return patch;
        };
        init => build = value;
    }
}

/// <summary>Patches that ship with the synth, so it never opens on a blank canvas.</summary>
public static partial class Presets
{
    /// <summary>
    /// Everything the engine ships, in the order the picker shows it: the blank
    /// canvas, then ideas, then interplay, then the big ones.
    /// </summary>
    public static IReadOnlyList<PatchPreset> All => [.. Shipped.Select(Fused)];

    /// <summary>
    /// A preset whose chains of Maths modules arrive folded into Expressions —
    /// see <see cref="ExpressionFusion"/> — and laid out again with what is left.
    /// </summary>
    public static PatchPreset Fused(PatchPreset preset) => preset with
    {
        Build = modules =>
        {
            var patch = ExpressionFusion.Fuse(preset.Build(modules), modules);

            PatchLayout.Arrange(patch, modules);
            return patch;
        },
    };

    private static IReadOnlyList<PatchPreset> Shipped =>
    [
        // --- nothing yet -------------------------------------------------------

        new("Empty", Empty,
            "The Output, with everything still to plug into it.",
            PresetKind.Blank),
        new("Picture in", PictureIn,
            "A photograph put through the same geometry a generated field goes through, once you choose one.",
            PresetKind.Blank),
        new("Clip", Clip,
            "A WAV file played and retriggered every two seconds, once you choose one.",
            PresetKind.Blank),

        // --- one idea, one sink ------------------------------------------------

        new("Plasma", Plasma,
            "Two sine fields crossed and read as hue — the hello world of video synths."),
        new("Kaleidoscope", Kaleidoscope,
            "Rotating wedges filled with noise that boils over time."),
        new("Grid", Grid,
            "Tile, mirror and polar in a row, so what each one does to the plane is separable."),
        new("Three channels", ThreeChannels,
            "One field read three times, a little apart: a color is three signals, and here they disagree."),
        new("Feedback tunnel", FeedbackTunnel,
            "Each frame re-read slightly rotated, scaled and dimmed, with fresh rings on top."),
        new("Trails", Trails,
            "A dot on a looping path and a Trails keeping where it has been, so a point draws a ribbon."),
        new("Loop", Loop,
            "A wire running backwards: a lowpass built from an add and a multiply, with its one number swept."),
        new("Two channels", TwoChannels,
            "Stereo from one voice: left and right fed differently rather than panned."),
        new("Staircase", Staircase,
            "A slope caught six times a second by a Sample & Hold, which makes steps, and steps are a tune."),
        new("Nebula", Nebula,
            "Everything the video side can do, folded, warped and trailing its own frames."),

        // --- the two sinks meeting ---------------------------------------------

        new("Drone", Drone,
            "One slow oscillator setting both the hue of the image and the tremolo on the tone.",
            PresetKind.Interplay),
        new("Sequence", Sequence,
            "One sequencer heard and seen at once: the steps are the tune and the color.",
            PresetKind.Interplay),
        new("Four voices", FourVoices,
            "Four faders that are one signal each, opening a voice and a band together.",
            PresetKind.Interplay),
        new("Heard", Heard,
            "A drum the picture listens to rather than being told about, through a Meter.",
            PresetKind.Interplay),
        new("Duck", Duck,
            "A pad that gets out of the way each time the kick hits, on a Scope drawing how far.",
            PresetKind.Interplay),
        new("Waveform", Waveform,
            "Sine, triangle, square and saw faded one into the next, on a Scope drawing the shape being heard.",
            PresetKind.Interplay),
        new("Sidebands", Sidebands,
            "One sine bending another's phase at audio rate, on an Analyzer showing the partials that grows.",
            PresetKind.Interplay),
        new("Ahead and behind", AheadAndBehind,
            "A Probe and a Scope on one signal, which is the only way to see how they differ.",
            PresetKind.Interplay),
        new("In key", InKey,
            "One noise field snapped to a pentatonic: heard as a melody, seen as the terraces it was cut into.",
            PresetKind.Interplay),
        new("Ring scan", RingScan,
            "A loop swept round a field at audio rate, so the picture is the waveform.",
            PresetKind.Interplay),

        // --- what one patch can be ---------------------------------------------

        new("Whole band", WholeBand,
            "A whole song from the engine's own modules: seven parts in a room, twelve phrases, one picture.",
            PresetKind.Showcase),
    ];

    /// <summary>
    /// One sequencer, heard and seen at once. The steps are the tune; where the
    /// sequence has got to is the color, and the gate that makes a rest silent
    /// is the same one that takes the light out of it.
    /// </summary>
    public static Patch Sequence(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Three notes a second, with the gate closed for the last third so that
        // two of the same note in a row are two notes. The notes themselves are
        // a list on the node rather than knobs on it (ADR-0038).
        var steps = b.Add("seq.notes", (1, 3f), (2, 0.66f));

        // Ear: the step is a note number, so it goes in where a note goes.
        var note = b.Add("audio.note");
        var tone = b.Add("osc.sine");
        var voiced = b.Add("math.mul");

        // Eye: rings whose count is the position in the pattern. Remapped rather
        // than multiplied, since index starts at zero and zero rings is a flat
        // field with no pattern in it.
        var depth = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1.5f), (4, 9f));
        var rings = b.Add("pattern.rings");
        var glow = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.05f), (4, 1f));

        // The gate dims the picture where it silences the tone, but only to four
        // tenths: multiplying by the gate itself blacks the screen for a third of
        // every step, which reads as a fault rather than as a pulse.
        var pulse = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.4f), (4, 1f));
        var lit = b.Add("math.mul");
        var color = b.Add("color.hsv", (1, 0.8f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(steps, 0, note, 0)
         .Wire(note, 0, tone, 1)
         .Wire(tone, 0, voiced, 0)
         .Wire(steps, 1, voiced, 1)
         .Wire(voiced, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(steps, 2, depth, 0)
         .Wire(depth, 0, rings, 2)
         .Wire(rings, 0, glow, 0)
         .Wire(steps, 1, pulse, 0)
         .Wire(glow, 0, lit, 0)
         .Wire(pulse, 0, lit, 1)
         .Wire(steps, 2, color, 0)
         .Wire(lit, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// A drum with the picture listening to it rather than being told about the
    /// beat.
    /// </summary>
    public static Patch Heard(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Two beats a second, and a gate short enough that the envelope decides
        // how long the drum is rather than the trigger — what decides the length
        // of a kick is the envelope and never the gate.
        var beat = b.Add("osc.pulse", (1, 2f), (3, 0.08f));

        var level = b.Add(NodeCatalog.AdsrTypeId, (1, -2.6f), (2, -0.9f), (3, 0f), (4, -1f));

        var pitch = b.Add("audio.frequency", (0, 70f));
        var tone = b.Add("osc.sine");
        var voiced = b.Add("math.mul");

        // The wire this patch is about: a signal on its way to the speakers,
        // read by something that hands the picture a number for it.
        var heard = b.Add(NodeCatalog.MeterTypeId, (1, -1.5f));

        var rings = b.Add("pattern.rings", (2, 5f));
        var glow = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.1f), (4, 1f));

        // A floor under the reading, so a silent moment is a dim picture rather
        // than a black one.
        var swell = b.Add("math.add", (1, 0.18f));
        var lit = b.Add("math.mul");
        var color = b.Add("color.hsv", (1, 0.75f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.6f));

        b.Wire(beat, 0, level, 0)
         .Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, voiced, 0)
         .Wire(level, 0, voiced, 1)
         .Wire(voiced, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(voiced, 0, heard, 0)
         .Wire(rings, 0, glow, 0)
         .Wire(glow, 0, lit, 0)
         .Wire(heard, 1, swell, 0)
         .Wire(swell, 0, lit, 1)
         .Wire(heard, 0, color, 0)
         .Wire(lit, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// A kick and a pad, and a Duck turning the pad down under every hit so the
    /// kick has the room to itself. The Scope draws the level the Duck applies,
    /// which falls with every hit and climbs back before the next.
    /// </summary>
    public static Patch Duck(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Heard's kick: two beats a second, its length the envelope's.
        var beat = b.Add("osc.pulse", (1, 2f), (3, 0.08f));
        var level = b.Add(NodeCatalog.AdsrTypeId, (1, -2.6f), (2, -0.9f), (3, 0f), (4, -1f));
        var pitch = b.Add("audio.frequency", (0, 55f));
        var body = b.Add("osc.sine");
        var kick = b.Add("math.mul");

        // A root and a fifth on two saws, held: nothing moves in the pad but the duck.
        var root = b.Add("audio.frequency", (0, 110f));
        var fifth = b.Add("audio.frequency", (0, 165f));
        var low = b.Add("osc.saw", (3, 0.25f));
        var high = b.Add("osc.saw", (3, 0.2f));
        var pad = b.Add("math.add");

        // The module this patch is about. Keyed by the kick's sound: down by four fifths
        // of how loud the kick is, and a fifth of a second to come back. The kick's
        // envelope would key it just as well.
        var duck = b.Add(NodeCatalog.DuckTypeId, (3, 0.8f), (5, -3f), (6, -0.7f));

        // The kick added back after the Duck, which never turns down what keys it.
        var mix = b.Add("math.add");

        // Two seconds across, which is four hits and the pad coming back after each.
        // The gain never goes below nought, so it sits above the middle line, with
        // room over one.
        var chart = b.Add(NodeCatalog.ScopeTypeId, (1, 0.3f), (2, 1.25f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(beat, 0, level, 0)
         .Wire(pitch, 0, body, 1)
         .Wire(body, 0, kick, 0)
         .Wire(level, 0, kick, 1)

         .Wire(root, 0, low, 1)
         .Wire(fifth, 0, high, 1)
         .Wire(low, 0, pad, 0)
         .Wire(high, 0, pad, 1)

         .Wire(pad, 0, duck, 0)
         .Wire(kick, 0, duck, 2)
         .Wire(duck, 0, mix, 0)
         .Wire(kick, 0, mix, 1)
         .Wire(mix, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(duck, 2, chart, 0)
         .Wire(chart, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>Just the Output, with everything still to plug into it.</summary>
    public static Patch Empty(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);
        b.Add(NodeCatalog.OutputTypeId);
        return b.Build();
    }

    /// <summary>Two sine fields crossed and read as hue — the "hello world" of video synths.</summary>
    public static Patch Plasma(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord");
        var time = b.Add("time");

        // A fifth of a radian a second into the phase below. Time is seconds and
        // nothing else, so a patch that wants less than that says so here.
        var slowly = b.Add("math.mul", (1, 0.2f));

        // Sine along x, and a second along y whose phase drifts with time.
        var horizontal = b.Add("osc.sine", (1, 1.5f));
        var vertical = b.Add("osc.sine", (1, 1.1f));

        var sum = b.Add("math.add");
        var hue = b.Add("math.remap", (1, -2f), (2, 2f), (3, 0f), (4, 1f));
        var color = b.Add("color.hsv", (1, 0.85f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(coord, 0, horizontal, 0)
         .Wire(coord, 1, vertical, 0)
         .Wire(time, 0, slowly, 0)
         .Wire(slowly, 0, vertical, 2)
         .Wire(horizontal, 0, sum, 0)
         .Wire(vertical, 0, sum, 1)
         .Wire(sum, 0, hue, 0)
         .Wire(hue, 0, color, 0)
         .Wire(color, 0, output, 0);

        return b.Build();
    }

    /// <summary>Rotating wedges filled with noise that boils over time.</summary>
    public static Patch Kaleidoscope(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // One clock and two speeds off it, rather than two clocks. Here only
        // because both speeds are scaled: the Rotate's own x and y are normalled
        // to Coordinates (ADR-0050).
        var clock = b.Add("time");
        var spin = b.Add("math.mul", (1, 0.15f));
        var drift = b.Add("math.mul", (1, 0.3f));

        var rotate = b.Add("space.rotate");
        var fold = b.Add("space.kaleidoscope", (2, 6f));
        var noise = b.Add("pattern.noise", (3, 2.5f));
        var color = b.Add("color.hsv", (1, 0.9f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, drift, 0)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, fold, 0)
         .Wire(rotate, 1, fold, 1)
         .Wire(fold, 0, noise, 0)
         .Wire(fold, 1, noise, 1)
         .Wire(drift, 0, noise, 2)
         .Wire(noise, 0, color, 0)
         .Wire(color, 0, output, 0);

        return b.Build();
    }

    /// <summary>
    /// One patch heard and seen at once. A single slow oscillator sets both the
    /// hue of the image and the tremolo on the tone, so the two sinks are
    /// visibly and audibly the same signal.
    /// </summary>
    public static Patch Drone(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // For the Rings' 'offset', the one socket in the patch that has to be
        // told to move — everything else is normalled (ADR-0050).
        var time = b.Add("time");

        // The shared control signal, remapped to 0..1 by amp and bias.
        var slow = b.Add("osc.sine", (1, 0.15f), (3, 0.5f), (4, 0.5f));

        // Ear.
        var pitch = b.Add("audio.frequency", (0, 110f));
        var tone = b.Add("osc.sine", (1, 110f));
        var tremolo = b.Add("math.mul");

        // Eye.
        var rings = b.Add("pattern.rings", (2, 3f));
        var tint = b.Add("color.hsv", (1, 0.85f));

        // Both halves land on the one block, which is what makes the shared
        // oscillator legible: two wires into the same module, from the same sine.
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.6f));

        b.Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, tremolo, 0)
         .Wire(slow, 0, tremolo, 1)
         .Wire(tremolo, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(time, 0, rings, 3)
         .Wire(slow, 0, tint, 0)
         .Wire(rings, 0, tint, 2)
         .Wire(tint, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// The picture played rather than drawn. A circle is swept round a field of
    /// rings at audio rate and what it passes over is the waveform, so the tone
    /// is not made by an oscillator anywhere — it is the image, read along a
    /// line.
    /// </summary>
    public static Patch RingScan(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // The field, and the only thing in the patch that makes the waveform.
        // 'freq' is the modulation index rather than a pitch: more rings under
        // the loop is more harmonics, at the same note.
        var rings = b.Add("pattern.rings", (2, 4f));

        // Where the loop is cut through the field, walked outward and back twice
        // a second. Kept clear of zero at the bottom of the sweep, because a loop
        // concentric with the rings reads a constant and is silent.
        var sweep = b.Add("osc.sine", (1, 0.2f));
        var where = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.2f), (4, 0.75f));

        var pitch = b.Add("audio.frequency", (0, 110f));

        // 'clock' is the sweep's own time base and takes no wire — it is a
        // domain, so it is normalled to Time; 'rate' is the pitch; 'radius' and
        // 'x' choose which loop through the field is read.
        var scan = b.Add(NodeCatalog.ScanTypeId, (3, 0.35f), (6, 1f));

        // Eye: the field under the trace, dim enough that the loop reads on top
        // of it rather than competing with it.
        var glow = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.05f), (4, 0.55f));
        var tint = b.Add("color.hsv", (1, 0.7f));
        var lit = b.Add("math.add");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.25f));

        // Ear: the field itself into the sweep, and out the other side as a
        // sample. Nothing between the picture and the speakers but the loop.
        b.Wire(rings, 0, scan, 0)
         .Wire(pitch, 0, scan, 2)
         .Wire(sweep, 0, where, 0)
         .Wire(where, 0, scan, 4)
         .Wire(scan, 0, output, NodeCatalog.OutputLeftPort)

         // Eye: the field, with the loop drawn over it.
         .Wire(rings, 0, glow, 0)
         .Wire(where, 0, tint, 0)
         .Wire(glow, 0, tint, 2)
         .Wire(tint, 0, lit, 0)
         .Wire(scan, 1, lit, 1)
         .Wire(lit, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// A noise field played as a melody and drawn as the terraces it is being
    /// snapped to: one Quantiser, in a pentatonic, feeding both sinks.
    /// </summary>
    public static Patch InKey(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // One tempo for the whole patch, because the two things it sets have to
        // be the same number: how often a note is plucked, and how often the
        // melody is allowed to move. 180 a minute is three a second.
        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 180f));

        // For the field's 'z', which is the one socket in the patch that has to
        // be told to move: 'z' is depth through the noise rather than a position
        // in it, so nothing is normalled to it (ADR-0050).
        var time = b.Add("time");

        var wander = b.Add("math.mul", (1, 0.3f));

        // The one module both sinks read, and the reason they hear and see the
        // same thing. Its x and y need no wire: on the screen they are the
        // pixel's own, and at the speakers there is no pixel and they are zero.
        var field = b.Add("pattern.noise", (3, 2.2f));

        // Two octaves from A2, which is low enough to sound like a bass line at
        // the bottom and high enough to sing at the top.
        var range = b.Add("math.remap", (1, 0f), (2, 1f), (3, 45f), (4, 69f));

        // A minor pentatonic — A C D E G. The scale is a set on the module
        // rather than sockets on it (ADR-0051).
        //
        // 'hold' is the difference between a melody and a glide: a pitch that
        // changes mid-note has no onset to mark it, so the ear takes it for the
        // note it was already on, sliding. The envelope's gate goes into 'hold'
        // as well, so a note is frozen for exactly as long as it sounds.
        var key = b.Add(NodeCatalog.QuantiserTypeId);
        ScaleExtra.Set(key, [0, 2, 4, 7, 9]);

        // Ear: the snapped note as a pitch, plucked three times a second.
        var note = b.Add("audio.note");
        var tone = b.Add("osc.sine");

        // 'width' is how much of each beat the trigger is shut for, so a small
        // one opens just after the beat and holds until just before the next.
        var beat = b.Add("osc.pulse", (3, 0.12f));

        // Percussive: nothing sustained, so a note has decayed to silence well
        // inside its own beat, and the pitch the Hold catches on the next one
        // lands on a note starting rather than on one still ringing.
        var pluck = b.Add(NodeCatalog.AdsrTypeId, (1, -2.4f), (2, -0.85f), (3, 0f), (4, -1.5f));
        var struck = b.Add("math.mul");

        // Eye: the snapped note as a hue, one turn of the wheel to the octave,
        // so a note is the same color wherever it turns up. Wrapped rather than
        // spread across the whole range — over two and a half octaves adjacent
        // notes would be a twentieth of the wheel apart, and the terraces would
        // read as a gradient with creases in it.
        var wheel = b.Add("math.mul", (1, 1f / 12f));
        var octave = b.Add("math.fract");

        // Warm at the bottom of the octave and cool at the top, over half the
        // wheel rather than all of it: a full turn puts red beside green beside
        // purple, which reads as a test card.
        var height = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.02f), (4, 0.6f));

        // And the field itself as brightness, unsnapped. This is the whole
        // demonstration: the gradient is what arrived and the bands are what the
        // Quantiser made of it, and both are on screen at once.
        var glow = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.22f), (4, 0.95f));
        var map = b.Add("color.hsv", (1, 0.6f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.55f));

        b.Wire(time, 0, wander, 0)
         .Wire(wander, 0, field, 2)

         .Wire(field, 0, range, 0)
         .Wire(range, 0, key, 0)

         // The beat into the Quantiser's 'hold' as well as the envelope's
         // 'gate'. That second wire is the one that keeps a note at one pitch.
         .Wire(beat, 0, key, 1)

         .Wire(key, 0, note, 0)
         .Wire(note, 0, tone, 1)
         .Wire(tempo, 0, beat, 1)
         .Wire(beat, 0, pluck, 0)
         .Wire(tone, 0, struck, 0)
         .Wire(pluck, 0, struck, 1)
         .Wire(struck, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(key, 0, wheel, 0)
         .Wire(wheel, 0, octave, 0)
         .Wire(octave, 0, height, 0)
         .Wire(field, 0, glow, 0)
         .Wire(height, 0, map, 0)
         .Wire(glow, 0, map, 2)
         .Wire(map, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }


    /// <summary>
    /// Everything the video side can do, in one patch: coordinates turned, folded
    /// into wedges, bent by a noise field read from inside the fold, taken as
    /// travelling rings, and laid over a trail of its own previous frames.
    /// </summary>
    public static Patch Nebula(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // One clock read at three speeds, so nothing in the picture quite lines
        // up with anything else and it does not visibly loop. The only source in
        // the patch: both geometry chains start from normalled coordinates.
        var clock = b.Add("time");
        var spin = b.Add("math.mul", (1, 0.05f));
        var boil = b.Add("math.mul", (1, 0.12f));
        var pulse = b.Add("math.mul", (1, 0.2f));

        var turn = b.Add("space.rotate");
        var fold = b.Add("space.kaleidoscope", (2, 8f));

        // Read from the folded plane, not the flat one, so the field is itself
        // symmetric — warping by anything asymmetric here would quietly undo the
        // fold and leave the picture looking like ordinary noise.
        var field = b.Add("pattern.noise", (3, 1.4f));

        var bend = b.Add("space.warp", (3, 0.5f));
        var bands = b.Add("pattern.rings", (2, 2.5f));

        // Rings are a sine, so most of the frame is dark and only the crests
        // survive as filaments.
        var filament = b.Add("math.smoothstep", (0, 0.15f), (1, 0.85f));

        // Hue drifts with the field and with time, wrapped back into 0..1.
        var drift = b.Add("math.add");
        var hue = b.Add("math.fract");

        var fresh = b.Add("color.hsv", (1, 0.85f));

        // The previous frame, zoomed out a hair and turned, so what is already on
        // screen spirals outward while new filaments arrive underneath it. A Trails
        // hands on the brighter of the two rather than a blend: a trail that is
        // brighter than the new frame keeps its brightness, which is what makes the
        // streaks read as trails rather than as a smeared copy.
        var trail = b.Add("feedback.trails", (3, 0.99f), (4, 0.015f), (7, 0.92f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(clock, 0, pulse, 0)
         .Wire(spin, 0, turn, 2)
         .Wire(turn, 0, fold, 0)
         .Wire(turn, 1, fold, 1)
         .Wire(fold, 0, field, 0)
         .Wire(fold, 1, field, 1)
         .Wire(boil, 0, field, 2)
         .Wire(fold, 0, bend, 0)
         .Wire(fold, 1, bend, 1)
         .Wire(field, 0, bend, 2)
         .Wire(bend, 0, bands, 0)
         .Wire(bend, 1, bands, 1)
         .Wire(pulse, 0, bands, 3)
         .Wire(bands, 0, filament, 2)
         .Wire(field, 0, drift, 0)
         .Wire(pulse, 0, drift, 1)
         .Wire(drift, 0, hue, 0)
         .Wire(hue, 0, fresh, 0)
         .Wire(filament, 0, fresh, 2)
         .Wire(fresh, 0, trail, 0)
         .Wire(trail, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// The camera-pointed-at-its-own-monitor patch: each frame is re-read
    /// slightly rotated, scaled and dimmed, with fresh rings fed in on top.
    /// </summary>
    public static Patch FeedbackTunnel(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var clock = b.Add("time");
        var spin = b.Add("math.mul", (1, 0.08f));
        var pulse = b.Add("math.mul", (1, 0.25f));

        var rotate = b.Add("space.rotate");
        var scale = b.Add("space.scale", (2, 1.05f));
        var previous = b.Add("feedback");
        var dim = b.Add("color.gain", (1, 0.95f), (2, 0f));

        // Fresh material: bright rings that travel outward.
        var rings = b.Add("pattern.rings", (2, 1.5f));
        var spark = b.Add("math.smoothstep", (0, 0.8f), (1, 1f));
        var tint = b.Add("color.hsv", (1, 1f));

        var combine = b.Add("math.max");
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, pulse, 0)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, scale, 0)
         .Wire(rotate, 1, scale, 1)
         .Wire(scale, 0, previous, 0)
         .Wire(scale, 1, previous, 1)
         .Wire(previous, 0, dim, 0)
         .Wire(pulse, 0, rings, 3)
         .Wire(rings, 0, spark, 2)
         .Wire(pulse, 0, tint, 0)
         .Wire(spark, 0, tint, 2)
         .Wire(dim, 0, combine, 0)
         .Wire(tint, 0, combine, 1)
         .Wire(combine, 0, output, 0);

        return b.Build();
    }

    /// <summary>
    /// Four voices, each with a fader, through one Mixer at each sink. The
    /// faders are the shared signal: level three on the chord is level three on
    /// the screen, so what fades up in the sound is the same thing that fades up
    /// in the picture.
    /// </summary>
    public static Patch FourVoices(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // For 'radius', which makes the eye's half of each voice a standing
        // field rather than a travelling tone. Everything else is normalled.
        var coord = b.Add("coord");

        var chord = b.Add("math.mixer");
        var picture = b.Add("math.mixer");

        var tame = b.Add("color.gain", (1, 0.6f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.25f));

        b.Wire(chord, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(picture, 0, tame, 0)
         .Wire(tame, 0, output, NodeCatalog.OutputColorPort);

        // An A major triad spread over two octaves, one voice a note. The band
        // count climbs with the voice, so how many rings are on screen is which
        // note is sounding. The fader rates share no common factor worth the
        // name, so the four never come up together twice.
        (float Note, float Bands, float Hue, float Rate, float Phase)[] voices =
        [
            (45f, 1f, 0.00f, 0.06f, 0.00f),
            (52f, 2f, 0.33f, 0.09f, 0.30f),
            (57f, 3f, 0.58f, 0.13f, 0.60f),
            (61f, 4f, 0.83f, 0.17f, 0.85f),
        ];

        for (var v = 0; v < voices.Length; v++)
        {
            var (note, bands, hue, rate, phase) = voices[v];

            // The fader. Amp and bias put a sine's -1..1 into the 0..1 a level
            // is edited within, so it opens and closes rather than going through
            // zero and coming back the other way up.
            var level = b.Add("osc.sine", (1, rate), (2, phase), (3, 0.5f), (4, 0.5f));

            // Ear: the note, and a sine at it.
            var pitch = b.Add("audio.note", (0, note));
            var tone = b.Add("osc.sine");

            // Eye: the same oscillator run over the radius instead of over the
            // clock, so it stands still as bands out from the centre rather than
            // travelling as a tone.
            var band = b.Add("osc.sine", (1, bands), (3, 0.5f), (4, 0.5f));
            var tint = b.Add("color.hsv", (0, hue));

            // Channel v of both mixers: the input, then the level beside it.
            var channel = v * 2;

            b.Wire(pitch, 0, tone, 1)
             .Wire(tone, 0, chord, channel)
             .Wire(level, 0, chord, channel + 1)

             .Wire(coord, 2, band, 0)
             .Wire(band, 0, tint, 2)
             .Wire(tint, 0, picture, channel)
             .Wire(level, 0, picture, channel + 1);
        }

        return b.Build();
    }

    /// <summary>The Transform's knobs, after its position.</summary>
    private const int TransformZoom = 2;

    private const int TransformAngle = 3;

    /// <summary>
    /// The three coordinate transforms nothing else in the box shows, in a row,
    /// so that what each does to the plane can be seen by taking it out.
    /// </summary>
    public static Patch Grid(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // The drift. 'in' takes no wire — it is a domain, normalled to Time.
        var slide = b.Add("osc.sine", (1, 0.06f));

        // x and y take no wire either: they are normalled to Coordinates, so
        // this reads the pixel's own position (ADR-0050).
        var move = b.Add("space.translate");
        var cells = b.Add("space.tile", (2, 3f));
        var fold = b.Add("space.mirror");
        var round = b.Add("space.polar");

        var squares = b.Add("pattern.checker", (2, 3f));

        // The angle as hue, so a spoke is a color rather than only a shape. It
        // arrives in radians and a hue is a turn, so it is remapped rather than
        // multiplied.
        var wheel = b.Add("math.remap", (1, -3.15f), (2, 3.15f), (3, 0f), (4, 1f));

        // The check is 0 or 1 and a picture that is half black reads as a fault,
        // so the dark squares are dim rather than absent — the same floor the
        // Sequence preset puts under its gate.
        var glow = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.14f), (4, 1f));

        var color = b.Add("color.hsv", (1, 0.7f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(slide, 0, move, 2)
         .Wire(move, 0, cells, 0)
         .Wire(move, 1, cells, 1)
         .Wire(cells, 0, fold, 0)
         .Wire(cells, 1, fold, 1)
         .Wire(fold, 0, round, 0)
         .Wire(fold, 1, round, 1)
         .Wire(round, 0, squares, 0)
         .Wire(round, 1, squares, 1)
         .Wire(round, 1, wheel, 0)
         .Wire(squares, 0, glow, 0)
         .Wire(wheel, 0, color, 0)
         .Wire(glow, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// The four wave shapes at one pitch, faded one into the next, and a Scope
    /// drawing what the speakers actually played — so a change of timbre is heard
    /// while the change of shape that makes it is on the screen.
    /// </summary>
    public static Patch Waveform(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var pitch = b.Add("audio.frequency", (0, 110f));
        var blend = b.Add("math.mixer");

        // About thirty milliseconds, which is three cycles and a bit: enough to
        // see that the shape repeats and few enough to see the shape. The knob is
        // in decades — see PortDisplay.Duration. Two faders are up at once and
        // together reach 1.4, so 'scale' leaves the chart room for both.
        var chart = b.Add(NodeCatalog.ScopeTypeId, (1, -1.52f), (2, 1.5f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.25f));

        b.Wire(blend, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(blend, 0, chart, 0)
         .Wire(chart, 0, output, NodeCatalog.OutputColorPort);

        // Smoothest first, so each shape in turn adds harmonics to the last. The
        // phases put a quarter of a turn between one fader's peak and the next,
        // and the sine's at the start, so the patch opens on the plainest tone.
        (string Shape, float Phase)[] shapes =
        [
            ("osc.sine", 0.25f),
            ("osc.triangle", 0f),
            ("osc.square", 0.75f),
            ("osc.saw", 0.5f),
        ];

        for (var s = 0; s < shapes.Length; s++)
        {
            var (shape, phase) = shapes[s];

            var tone = b.Add(shape);

            // The fader: the top half of a slow sine. A Mixer's level is a
            // multiply and nothing else, so the bottom half would bring the
            // shape back upside down rather than leave it out.
            var turn = b.Add("osc.sine", (1, 0.08f), (2, phase));
            var fader = b.Add("math.max", (1, 0f));

            b.Wire(pitch, 0, tone, 1)
             .Wire(tone, 0, blend, s * 2)
             .Wire(turn, 0, fader, 0)
             .Wire(fader, 0, blend, s * 2 + 1);
        }

        return b.Build();
    }

    /// <summary>
    /// A Probe and a Scope reading the same wire, one above the other, which is
    /// the only arrangement in which the difference between them is visible.
    /// </summary>
    public static Patch AheadAndBehind(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Here for 'y', which is what the two charts are divided on. Nothing
        // else in the patch needs a source.
        var coord = b.Add("coord");

        // The sweep, and the reason the two charts differ at all.
        var sweep = b.Add("osc.sine", (1, 0.4f));
        var hz = b.Add("math.remap", (1, -1f), (2, 1f), (3, 90f), (4, 320f));
        var tone = b.Add("osc.saw");

        var ahead = b.Add(NodeCatalog.ProbeTypeId, (1, -1.6f));
        var behind = b.Add(NodeCatalog.ScopeTypeId, (1, -1.6f));

        // 0 below the middle of the frame and 1 above it, which is what picks
        // between the two charts. Blend takes b where t is 1, so the Probe is up.
        var half = b.Add("math.step");
        var split = b.Add("color.mix");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.3f));

        b.Wire(sweep, 0, hz, 0)
         .Wire(hz, 0, tone, 1)
         .Wire(tone, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(tone, 0, ahead, 0)
         .Wire(tone, 0, behind, 0)
         .Wire(coord, 1, half, 1)
         .Wire(behind, 0, split, 0)
         .Wire(ahead, 0, split, 1)
         .Wire(half, 0, split, 2)
         .Wire(split, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// The sample player, wired the three ways it is worth wiring: played
    /// forward on the clock, restarted by a trigger, and scrubbed by a signal.
    /// </summary>
    public static Patch Clip(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Every two seconds, and a gate narrow enough that what is heard is the
        // clip rather than the trigger. Its 'in' takes no wire: it is a domain,
        // normalled to Time (ADR-0050).
        var again = b.Add("osc.pulse", (1, 0.5f), (3, 0.02f));

        // 'in' takes no wire either, for the same reason — so the clip plays
        // forward at its own speed from wherever the last edge left the zero.
        var clip = b.Add(NodeCatalog.SampleTypeId, (1, 0.9f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        b.Wire(again, 0, clip, 2)
         .Wire(clip, 0, output, NodeCatalog.OutputLeftPort);

        return b.Build();
    }

    /// <summary>
    /// A photograph read as a field, and put through the same geometry a
    /// generated one goes through.
    /// </summary>
    public static Patch PictureIn(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var clock = b.Add("time");
        var spin = b.Add("math.mul", (1, 0.05f));
        var boil = b.Add("math.mul", (1, 0.15f));

        // Breathing rather than fixed, so the frame is never quite the same twice
        // and the edges of the picture come in and out of the view.
        var breath = b.Add("osc.sine", (1, 0.05f));
        var zoom = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.85f), (4, 1.4f));

        // x and y take no wire anywhere in this chain: each is normalled to
        // Coordinates, so the chain reads the pixel's own position and hands the
        // moved position on.
        var scale = b.Add("space.scale");
        var turn = b.Add("space.rotate");

        var field = b.Add("pattern.noise", (3, 1.8f));
        var bend = b.Add("space.warp", (3, 0.12f));

        var photo = b.Add(NodeCatalog.PictureTypeId);

        // A little contrast on the way out, so a flat photograph still reads as
        // one that is being done something to.
        var graded = b.Add("color.gain", (1, 1.15f), (2, -0.05f));

        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(breath, 0, zoom, 0)
         .Wire(zoom, 0, scale, 2)
         .Wire(scale, 0, turn, 0)
         .Wire(scale, 1, turn, 1)
         .Wire(spin, 0, turn, 2)
         .Wire(boil, 0, field, 2)
         .Wire(turn, 0, bend, 0)
         .Wire(turn, 1, bend, 1)
         .Wire(field, 0, bend, 2)
         .Wire(bend, 0, photo, 0)
         .Wire(bend, 1, photo, 1)
         .Wire(photo, 0, graded, 0)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// One wire running backwards, which is a lowpass built by hand out of an add
    /// and a multiply, with the one number that is its cutoff swept so that it
    /// can be heard to be one.
    /// </summary>
    public static Patch Loop(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var pitch = b.Add("audio.frequency", (0, 110f));

        // The thing being filtered. A square, because its corners are what a
        // lowpass visibly and audibly takes off.
        var source = b.Add("osc.square");

        // How much of the last evaluation is kept, which is the whole of the
        // filter: the nearer one, the lower the cutoff. The useful range is all
        // in the last tenth, so that is all the sweep covers — from a square
        // with its corners on down to little more than its fundamental.
        var sweep = b.Add("osc.sine", (1, 0.1f));
        var keep = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.92f), (4, 0.996f));

        // What is let in is what is not kept, so the two shares make one and the
        // loop comes out as loud as it went in however far the sweep has got.
        var share = b.Add("math.sub", (0, 1f));
        var fresh = b.Add("math.mul");

        var sum = b.Add("math.add");
        var kept = b.Add("math.mul");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.2f));

        // What the kept share is added back into is the wire that closes the loop,
        // so that is the one which runs backwards and carries the evaluation
        // before — the whole of what makes this a filter rather than a ring of
        // wires with nothing in it. The canvas draws that one dashed.
        b.Wire(pitch, 0, source, 1)
         .Wire(sweep, 0, keep, 0)
         .Wire(keep, 0, share, 1)
         .Wire(source, 0, fresh, 0)
         .Wire(share, 0, fresh, 1)
         .Wire(fresh, 0, sum, 0)
         .Wire(kept, 0, sum, 1)
         .Wire(sum, 0, kept, 0)
         .Wire(keep, 0, kept, 1)
         .Wire(sum, 0, output, NodeCatalog.OutputLeftPort);

        return b.Build();
    }

    /// <summary>
    /// Two channels that are two signals, rather than one signal made quieter on
    /// one side.
    /// </summary>
    public static Patch TwoChannels(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // A2. The second Note takes the first's snapped 'note' output rather than
        // the knob again, so the two cannot drift apart by an edit.
        var note = b.Add("audio.note", (0, 45f));
        var twin = b.Add("audio.note", (2, 9f));

        var left = b.Add("osc.saw", (3, 0.7f));
        var right = b.Add("osc.saw", (3, 0.7f));

        // One envelope for both, opened by one pulse: the two ears are the same
        // note, and only the tuning of it differs.
        var beat = b.Add("osc.pulse", (1, 1.5f), (3, 0.3f));
        var shape = b.Add(NodeCatalog.AdsrTypeId, (1, -2f), (2, -0.9f), (3, 0.4f), (4, -0.8f));

        var voiceL = b.Add("math.mul");
        var voiceR = b.Add("math.mul");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.55f));

        b.Wire(note, 1, twin, 0)
         .Wire(note, 0, left, 1)
         .Wire(twin, 0, right, 1)
         .Wire(beat, 0, shape, 0)
         .Wire(left, 0, voiceL, 0)
         .Wire(shape, 0, voiceL, 1)
         .Wire(right, 0, voiceR, 0)
         .Wire(shape, 0, voiceR, 1)
         .Wire(voiceL, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(voiceR, 0, output, NodeCatalog.OutputRightPort);

        return b.Build();
    }

    /// <summary>
    /// One field read at three sizes, one reading to each of red, green and
    /// blue: a color is three signals, and nothing says they have to agree.
    /// </summary>
    public static Patch ThreeChannels(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var clock = b.Add("time");
        var spin = b.Add("math.mul", (1, 0.06f));
        var flow = b.Add("math.mul", (1, 0.15f));

        // How far apart the three readings are, from not at all to a tenth. At
        // nothing the picture is white lines on black, which is the point of
        // starting there: the colors are made by the disagreement and by
        // nothing else.
        var sweep = b.Add("osc.sine", (1, 0.08f));
        var apart = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0f), (4, 0.1f));
        var larger = b.Add("math.add", (0, 1f));
        var smaller = b.Add("math.sub", (0, 1f));

        // The plane all three read: folded, and then pushed off the middle so
        // the rings are centred six times round it rather than once.
        var turn = b.Add("space.rotate");
        var fold = b.Add("space.kaleidoscope", (2, 6f));
        var aside = b.Add("space.translate", (2, 0.55f));

        var color = b.Add("color.rgb");
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, flow, 0)
         .Wire(spin, 0, turn, 2)
         .Wire(turn, 0, fold, 0)
         .Wire(turn, 1, fold, 1)
         .Wire(fold, 0, aside, 0)
         .Wire(fold, 1, aside, 1)
         .Wire(sweep, 0, apart, 0)
         .Wire(apart, 0, larger, 1)
         .Wire(apart, 0, smaller, 1)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        // Red through a Scale a little over one, green straight, blue through
        // one a little under. A scale moves a point further the further out it
        // is, so the fringes open towards the edges as a lens's do.
        NodeInstance?[] sizes = [larger, null, smaller];

        for (var channel = 0; channel < sizes.Length; channel++)
        {
            var rings = b.Add("pattern.rings", (2, 2.5f));

            // Only the crests, so a channel is a line rather than a band and
            // three of them side by side stay three.
            var line = b.Add("math.smoothstep", (0, 0.75f), (1, 1f));

            if (sizes[channel] is { } size)
            {
                var scale = b.Add("space.scale");

                b.Wire(aside, 0, scale, 0)
                 .Wire(aside, 1, scale, 1)
                 .Wire(size, 0, scale, 2)
                 .Wire(scale, 0, rings, 0)
                 .Wire(scale, 1, rings, 1);
            }
            else
            {
                b.Wire(aside, 0, rings, 0)
                 .Wire(aside, 1, rings, 1);
            }

            b.Wire(flow, 0, rings, 3)
             .Wire(rings, 0, line, 2)
             .Wire(line, 0, color, channel);
        }

        return b.Build();
    }

    /// <summary>
    /// A dot steered round a looping path by two sines, and a Trails keeping
    /// where it has been.
    /// </summary>
    public static Patch Trails(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Here because the dot is a distance from somewhere other than the
        // middle, which takes the pixel's own position to subtract from.
        var coord = b.Add("coord");
        var clock = b.Add("time");

        // Three to two, a quarter of a turn apart: a path that crosses itself
        // and takes ten seconds to come round.
        var across = b.Add("osc.sine", (1, 0.3f), (3, 0.5f));
        var down = b.Add("osc.sine", (1, 0.2f), (2, 0.25f), (3, 0.3f));

        var fromX = b.Add("math.sub");
        var fromY = b.Add("math.sub");
        var distance = b.Add("math.hypot");

        // The edges the wrong way round, which is a Smoothstep read backwards: 1
        // inside the dot and 0 outside it.
        var dot = b.Add("math.smoothstep", (0, 0.09f), (1, 0.03f));

        var drift = b.Add("math.mul", (1, 0.08f));
        var tint = b.Add("color.hsv", (1, 0.85f));

        // 'zoom' under one pushes what is already on screen outward and 'angle'
        // turns it, so the path the dot leaves swells into a ribbon rather than
        // fading where it lay.
        var trail = b.Add("feedback.trails", (3, 0.985f), (4, 0.015f), (7, 0.99f));
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(coord, 0, fromX, 0)
         .Wire(across, 0, fromX, 1)
         .Wire(coord, 1, fromY, 0)
         .Wire(down, 0, fromY, 1)
         .Wire(fromX, 0, distance, 0)
         .Wire(fromY, 0, distance, 1)
         .Wire(distance, 0, dot, 2)
         .Wire(clock, 0, drift, 0)
         .Wire(drift, 0, tint, 0)
         .Wire(dot, 0, tint, 2)
         .Wire(tint, 0, trail, 0)
         .Wire(trail, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }

    /// <summary>
    /// A Sample &amp; Hold catching a slow slope on every tick of a clock: what
    /// comes out is a staircase, and a staircase put through a scale is a tune.
    /// </summary>
    public static Patch Staircase(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Six ticks a second, and the one clock: it catches the pitch and it
        // plucks the note, so a note cannot change pitch while it sounds.
        var clock = b.Add("osc.pulse", (1, 6f));

        // The slope: two sines whose rates share nothing, so the staircase cut
        // from it climbs and falls without ever quite repeating.
        var slow = b.Add("osc.sine", (1, 0.11f));
        var quick = b.Add("osc.sine", (1, 0.37f), (3, 0.5f));
        var slope = b.Add("math.add");

        // Three octaves from A2.
        var range = b.Add("math.remap", (1, -1.5f), (2, 1.5f), (3, 45f), (4, 81f));

        var stair = b.Add(NodeCatalog.HoldTypeId);

        // A minor pentatonic. A held value is still any number at all — what
        // makes it a note is this.
        var key = b.Add("audio.tune");
        ScaleExtra.Set(key, [0, 2, 4, 7, 9]);

        var tone = b.Add("osc.triangle");
        var pluck = b.Add(NodeCatalog.AdsrTypeId, (1, -2.52f), (2, -0.92f), (3, 0.2f), (4, -1.22f));
        var voice = b.Add("math.mul");

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(slow, 0, slope, 0)
         .Wire(quick, 0, slope, 1)
         .Wire(slope, 0, range, 0)
         .Wire(range, 0, stair, 0)
         .Wire(clock, 0, stair, 1)
         .Wire(stair, 0, key, 0)
         .Wire(key, 0, tone, 1)
         .Wire(clock, 0, pluck, 0)
         .Wire(tone, 0, voice, 0)
         .Wire(pluck, 0, voice, 1)
         .Wire(voice, 0, output, NodeCatalog.OutputLeftPort);

        return b.Build();
    }

    /// <summary>
    /// Frequency modulation built by hand: one sine into another's phase, the
    /// amount struck and left to fall, and an Analyzer on what comes out.
    /// </summary>
    public static Patch Sidebands(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Every two seconds, and nothing sustained: the note is all decay, which
        // is what lets the partials be watched leaving.
        var beat = b.Add("osc.pulse", (1, 0.5f), (3, 0.1f));
        var strike = b.Add(NodeCatalog.AdsrTypeId, (1, -2.7f), (2, 0.176f), (3, 0f), (4, -0.52f));

        // How hard the next strike bends the carrier, wandering between a
        // mellow note and a clangorous one so no two strikes are the same bell.
        var sweep = b.Add("osc.sine", (1, 0.07f));
        var depth = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.2f), (4, 1.6f));

        // A3, and a modulator three and a half times above it. Not a whole
        // number, so the partials it makes are not harmonics — which is the
        // difference between an organ and a bell.
        var root = b.Add("audio.note", (0, 57f));
        var ratio = b.Add("math.mul", (1, 3.5f));
        var modulator = b.Add("osc.sine");

        // The index. It falls with the strike, so the note starts bright and
        // rings pure: on the chart a comb of peaks closing up into one.
        var struck = b.Add("math.mul");
        var index = b.Add("math.mul");

        var carrier = b.Add("osc.sine");
        var voice = b.Add("math.mul");

        var chart = b.Add(NodeCatalog.AnalyzerTypeId, (1, -1.3f), (2, 72f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.3f));

        b.Wire(beat, 0, strike, 0)
         .Wire(sweep, 0, depth, 0)
         .Wire(root, 0, ratio, 0)
         .Wire(ratio, 0, modulator, 1)
         .Wire(strike, 0, struck, 0)
         .Wire(depth, 0, struck, 1)
         .Wire(modulator, 0, index, 0)
         .Wire(struck, 0, index, 1)
         .Wire(root, 0, carrier, 1)
         .Wire(index, 0, carrier, 2)
         .Wire(carrier, 0, voice, 0)
         .Wire(strike, 0, voice, 1)
         .Wire(voice, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(voice, 0, chart, 0)
         .Wire(chart, 0, output, NodeCatalog.OutputColorPort);

        return b.Build();
    }
}
