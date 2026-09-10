namespace Flyback.Core.Graph;

/// <summary>
/// What kind of thing a preset is, which is what the picker groups by.
/// </summary>
public enum PresetKind
{
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

    /// <summary>The Output and nothing else, which is not teaching anything.</summary>
    Blank,
}

/// <summary>
/// A patch to start from, and how it is offered. Built on demand, because a
/// preset from a plugin needs that plugin's modules in the catalogue.
/// </summary>
/// <param name="Description">One line saying what the patch is for, shown under its name.</param>
public sealed record PatchPreset(
    string Name,
    Func<ModuleCatalog, Patch> Build,
    string Description = "",
    PresetKind Kind = PresetKind.Idea);

/// <summary>Patches that ship with the synth, so it never opens on a blank canvas.</summary>
public static class Presets
{
    /// <summary>
    /// Everything the engine ships, in the order the picker shows it: ideas,
    /// then interplay, then the big ones, then the blank canvas.
    /// </summary>
    public static IReadOnlyList<PatchPreset> All =>
    [
        // --- one idea, one sink ------------------------------------------------

        new("Plasma", Plasma,
            "Two sine fields crossed and read as hue — the hello world of video synths."),
        new("Kaleidoscope", Kaleidoscope,
            "Rotating wedges filled with noise that boils over time."),
        new("Grid", Grid,
            "Tile, mirror and polar in a row, so what each one does to the plane is separable."),
        new("Feedback tunnel", FeedbackTunnel,
            "Each frame re-read slightly rotated, scaled and dimmed, with fresh rings on top."),
        new("Picture in", PictureIn,
            "A photograph put through the same geometry a generated field goes through."),
        new("Clip", Clip,
            "A WAV file played, scrubbed and retriggered."),
        new("Loop", Loop,
            "A Unit Delay closing a cycle, which is how an integrator and a comb are built."),
        new("Two channels", TwoChannels,
            "Stereo from one voice: left and right fed differently rather than panned."),

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
        new("Waveform", Waveform,
            "A Scope charting what the speakers actually played, rather than what the screen computes.",
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

        new("Nebula", Nebula,
            "Everything the video side can do, folded, warped and trailing its own frames.",
            PresetKind.Showcase),
        new("Played", Played,
            "The one preset you have to play: a keyboard driving pitch, envelope and timbre.",
            PresetKind.Showcase),
        new("Whole band", WholeBand,
            "Four instruments off four sequencers, and one picture off three of them.",
            PresetKind.Showcase),

        new("Empty", Empty,
            "The Output, with everything still to plug into it.",
            PresetKind.Blank),
    ];

    public static Patch Default() => Plasma(NodeCatalog.Current);

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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.5f));

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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.6f));

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
    /// The one preset you have to play. Nothing in it moves on its own: a MIDI
    /// In drives the pitch, the envelope and the timbre, and with no key down it
    /// is silent and the picture is dim.
    /// </summary>
    public static Patch Played(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // No knobs on it at all. Which keyboard it listens to is the one thing it
        // carries, and a fresh one carries the computer's own.
        var keys = b.Add(NodeCatalog.MidiTypeId);
        keys.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiExtra.IndexField] = 1f,
        });

        // Ear. The note number goes in where a note number goes, and comes out
        // as hertz.
        var note = b.Add("audio.note");

        // A pulse rather than a saw, because its width is somewhere for the
        // held value to go. 'in' takes no wire: it runs on the clock every
        // domain socket is normalled to (ADR-0050).
        var tone = b.Add("osc.pulse");

        // A pluck: quick on, most of the way down in a fifth of a second, and
        // held at half while the key is. The times are decades of seconds — see
        // PortDisplay.Duration — so -2.4 is about four milliseconds.
        var env = b.Add(NodeCatalog.AdsrTypeId, (1, -2.4f), (2, -0.7f), (3, 0.5f), (4, -1f));

        var voiced = b.Add("math.mul");

        // The timbre, which is what 'trigger' is here for. A clock into 'z'
        // makes the field wander; x and y are nothing at the speakers.
        var clock = b.Add("time");
        var drift = b.Add("math.mul", (1, 3f));
        var wander = b.Add("pattern.noise");
        var caught = b.Add(NodeCatalog.HoldTypeId);

        // Never all the way to either end: a duty cycle of nought or one is
        // silence, and a note that happened to catch one would simply not sound.
        var width = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.12f), (4, 0.88f));

        // Eye. Two readings of the note number — its color, and how finely the
        // rings are drawn — over the two octaves either side of middle C. Held
        // into that range first, so a wider keyboard cannot wrap the hue round
        // to a color the other end is using, and so an unplayed patch rests at
        // the bottom of the range rather than wherever nought lands.
        var range = b.Add("math.clamp", (1, 36f), (2, 84f));

        var hue = b.Add("math.remap", (1, 36f), (2, 84f), (3, 0.55f), (4, 0f));
        var fineness = b.Add("math.remap", (1, 36f), (2, 84f), (3, 2f), (4, 11f));

        var rings = b.Add("pattern.rings");
        var glow = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.1f), (4, 1f));

        // What the envelope hands the screen is its gate, since an envelope has
        // no memory on the video path. Dimmed to a quarter between notes.
        var lift = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.25f), (4, 1f));
        var lit = b.Add("math.mul");
        var skin = b.Add("color.hsv", (1, 0.8f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.6f));

        b.Wire(keys, 0, note, 0)
         .Wire(note, 0, tone, 1)
         .Wire(keys, 1, env, 0)
         .Wire(tone, 0, voiced, 0)
         .Wire(env, 0, voiced, 1)
         .Wire(voiced, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(clock, 0, drift, 0)
         .Wire(drift, 0, wander, 2)
         .Wire(wander, 0, caught, 0)
         .Wire(keys, 3, caught, 1)
         .Wire(caught, 0, width, 0)
         .Wire(width, 0, tone, 3)

         .Wire(keys, 0, range, 0)
         .Wire(range, 0, hue, 0)
         .Wire(range, 0, fineness, 0)
         .Wire(fineness, 0, rings, 2)
         .Wire(rings, 0, glow, 0)
         .Wire(env, 0, lift, 0)
         .Wire(glow, 0, lit, 0)
         .Wire(lift, 0, lit, 1)
         .Wire(hue, 0, skin, 0)
         .Wire(lit, 0, skin, 2)
         .Wire(skin, 0, output, NodeCatalog.OutputColorPort);

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
    /// visibly and audibly the same signal. Switch Audio on to hear it.
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
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.6f));

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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.45f));

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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.55f));

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
        // screen spirals outward while new filaments arrive underneath it.
        var widen = b.Add("space.scale", (2, 0.99f));
        var swirl = b.Add("space.rotate", (2, 0.015f));
        var previous = b.Add("feedback");
        var trail = b.Add("color.gain", (1, 0.92f), (2, 0f));

        // Max rather than a blend: a trail that is brighter than the new frame
        // keeps its brightness, which is what makes the streaks read as trails
        // rather than as a smeared copy.
        var combine = b.Add("math.max");
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
         .Wire(widen, 0, swirl, 0)
         .Wire(widen, 1, swirl, 1)
         .Wire(swirl, 0, previous, 0)
         .Wire(swirl, 1, previous, 1)
         .Wire(previous, 0, trail, 0)
         .Wire(trail, 0, combine, 0)
         .Wire(fresh, 0, combine, 1)
         .Wire(combine, 0, output, NodeCatalog.OutputColorPort);

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
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.25f));

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

    /// <summary>
    /// Four instruments off four sequencers, and one picture off three of them.
    /// A bass line, a two-oscillator lead with a fifth over it, a kick and a
    /// hi-hat, mixed to a stereo pair — and the same steps that play them are
    /// what turns, folds, colors and lights the image.
    /// </summary>
    public static Patch WholeBand(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // --- the clock -------------------------------------------------------

        // Here for the things that have to be told to move and are not an 'in':
        // the Noise's z, three drift rates, and the hash the hats are made of.
        var clock = b.Add("time");

        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 112f));
        var eighths = b.Add("math.mul", (1, 2f));
        var sixteenths = b.Add("math.mul", (1, 4f));

        b.Group("Clock", clock, tempo, eighths, sixteenths);

        // --- the sequences ---------------------------------------------------

        // Am, G, F, E over two bars of eighths, with the last chord held. The
        // lengths are uneven and that is the groove: the dotted eighth into a
        // sixteenth at the top of each bar is what stops it walking.
        var bassSeq = b.Add("seq.notes", (2, 0.55f), (3, 0.02f));
        StepsExtra.Set(bassSeq,
        [
            new Step(33f, 1.5f), new Step(33f, 0.5f, 0.55f),
            new Step(40f, 1f, 0.8f), new Step(33f, 1f, 0.65f),
            new Step(31f, 1.5f), new Step(31f, 0.5f, 0.55f),
            new Step(38f, 1f, 0.8f), new Step(31f, 1f, 0.65f),
            new Step(29f, 1.5f), new Step(36f, 0.5f, 0.6f),
            new Step(29f, 2f, 0.85f), new Step(28f, 4f),
        ]);

        // Twenty sixteenths — five beats, against the bass's eight. A rest is a
        // volume rather than a note, so the pitch stays where it was and the
        // notes either side of it are one phrase.
        var leadSeq = b.Add("seq.notes", (2, 0.62f), (3, 0.045f));
        StepsExtra.Set(leadSeq,
        [
            new Step(69f), new Step(72f, 1f, 0.8f), new Step(76f, 1f, 0.9f), new Step(72f, 1f, 0.6f),
            new Step(77f), new Step(76f, 1f, 0.85f), new Step(76f, 1f, 0f), new Step(74f, 1f, 0.9f),
            new Step(71f, 1f, 0.8f), new Step(74f, 1f, 0.7f), new Step(79f), new Step(77f, 1f, 0.85f),
            new Step(76f, 1f, 0.9f), new Step(74f, 1f, 0.6f), new Step(72f, 1f, 0.95f), new Step(72f, 1f, 0f),
            new Step(71f, 1f, 0.85f), new Step(69f), new Step(67f, 1f, 0.7f), new Step(69f, 1f, 0.8f),
        ]);

        // The drum pattern, as volumes: four beats, a ghost off the second and
        // another at the end of the bar. The step's own value is spare here, so
        // the volumes carry the whole pattern.
        var kickSeq = b.Add("seq.values", (2, 0.32f), (3, 0.01f));
        StepsExtra.Set(kickSeq,
        [
            new Step(0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.9f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.5f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.95f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.85f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.55f), new Step(0f, 1f, 0f),
        ]);

        // Here the value is used, which is what needs a Sequencer rather than a
        // Note Sequencer: it goes to the decay knob of the hat's envelope, so a
        // high step rings and a low one is a tick.
        var hatSeq = b.Add("seq.values", (2, 0.4f), (3, 0.01f));
        StepsExtra.Set(hatSeq,
        [
            new Step(0.15f, 1f, 0.9f), new Step(0.1f, 1f, 0.35f),
            new Step(0.15f, 1f, 0.6f), new Step(0.1f, 1f, 0.3f),
            new Step(0.15f, 1f, 0.85f), new Step(0.1f, 1f, 0.35f),
            new Step(0.55f, 1f, 0.7f), new Step(0.1f, 1f, 0.3f),
            new Step(0.15f, 1f, 0.9f), new Step(0.1f, 1f, 0.35f),
            new Step(0.15f, 1f, 0.6f), new Step(0.1f, 1f, 0.3f),
            new Step(0.15f, 1f, 0.8f), new Step(0.1f, 1f, 0.4f),
            new Step(1f, 1f, 0.85f), new Step(0.1f, 1f, 0.5f),
        ]);

        b.Wire(tempo, 0, eighths, 0)
         .Wire(tempo, 0, sixteenths, 0)
         .Wire(eighths, 0, bassSeq, 1)
         .Wire(sixteenths, 0, leadSeq, 1)
         .Wire(sixteenths, 0, kickSeq, 1)
         .Wire(sixteenths, 0, hatSeq, 1);

        b.Group("Sequences", bassSeq, leadSeq, kickSeq, hatSeq);

        // --- bass ------------------------------------------------------------

        // A saw at the note and a sine an octave under it. The sub is a second
        // Note rather than an oscillator at half the frequency, because an
        // octave is a socket on the module that knows what a note number is.
        var bassNote = b.Add("audio.note");
        var subNote = b.Add("audio.note", (1, -1f));

        var bassSaw = b.Add("osc.saw", (3, 0.8f));
        var bassSub = b.Add("osc.sine", (3, 0.6f));
        var bassSum = b.Add("math.add");

        var bassEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -3f), (2, -1.1f), (3, 0.35f), (4, -1.2f));

        var bassVca = b.Add("math.mul");

        // Overdriven then clipped, which is the cheapest waveshaper there is: a
        // flattened saw is a saw with more harmonics in it. Only the bass, since
        // the same pair across the lead would take its envelope off it.
        var bassHot = b.Add("math.mul", (1, 2.4f));
        var bassOut = b.Add("math.clamp", (1, -1f), (2, 1f));

        b.Wire(bassSeq, 0, bassNote, 0)
         .Wire(bassSeq, 0, subNote, 0)
         .Wire(bassNote, 0, bassSaw, 1)
         .Wire(subNote, 0, bassSub, 1)
         .Wire(bassSaw, 0, bassSum, 0)
         .Wire(bassSub, 0, bassSum, 1)
         .Wire(bassSeq, 1, bassEnv, 0)
         .Wire(bassSum, 0, bassVca, 0)
         .Wire(bassEnv, 0, bassVca, 1)
         .Wire(bassVca, 0, bassHot, 0)
         .Wire(bassHot, 0, bassOut, 0);

        b.Group("Bass", bassNote, subNote, bassSaw, bassSub, bassSum, bassEnv, bassVca,
            bassHot, bassOut);

        // --- lead ------------------------------------------------------------

        var leadNote = b.Add("audio.note");

        // The twin, off the first Note's 'note' output: the detune goes on after
        // the snap because cents are the one control that can sit between two
        // semitones. Swung by a slow sine, so the beating rate keeps changing.
        var vibrato = b.Add("osc.sine", (1, 5.4f), (3, 9f));
        var wide = b.Add("audio.note");

        var leadA = b.Add("osc.saw", (3, 0.7f));
        var leadB = b.Add("osc.saw", (3, 0.7f));

        // A fifth over the tune, on a triangle so it fills rather than competes.
        // Adding seven before the Note is the interval.
        var fifth = b.Add("math.add", (1, 7f));
        var fifthNote = b.Add("audio.note");
        var fifthOsc = b.Add("osc.triangle", (3, 0.5f));
        var swell = b.Add("osc.sine", (1, 0.043f), (3, 0.5f), (4, 0.5f));
        var fifthLevel = b.Add("math.mul");

        // Left and right differ in which saw they carry and in nothing else,
        // which is where the width comes from.
        var stackL = b.Add("math.add");
        var stackR = b.Add("math.add");

        var leadEnv = b.Add(NodeCatalog.AdsrTypeId,
            (1, -2.7f), (2, -1.15f), (3, 0.28f), (4, -1.4f));

        var voiceL = b.Add("math.mul");
        var voiceR = b.Add("math.mul");

        b.Wire(leadSeq, 0, leadNote, 0)
         .Wire(leadNote, 1, wide, 0)
         .Wire(vibrato, 0, wide, 2)
         .Wire(leadNote, 0, leadA, 1)
         .Wire(wide, 0, leadB, 1)

         .Wire(leadSeq, 0, fifth, 0)
         .Wire(fifth, 0, fifthNote, 0)
         .Wire(fifthNote, 0, fifthOsc, 1)
         .Wire(fifthOsc, 0, fifthLevel, 0)
         .Wire(swell, 0, fifthLevel, 1)

         .Wire(leadA, 0, stackL, 0)
         .Wire(fifthLevel, 0, stackL, 1)
         .Wire(leadB, 0, stackR, 0)
         .Wire(fifthLevel, 0, stackR, 1)

         .Wire(leadSeq, 1, leadEnv, 0)
         .Wire(stackL, 0, voiceL, 0)
         .Wire(leadEnv, 0, voiceL, 1)
         .Wire(stackR, 0, voiceR, 0)
         .Wire(leadEnv, 0, voiceR, 1);

        b.Group("Lead", leadNote, vibrato, wide, leadA, leadB, fifth, fifthNote, fifthOsc,
            swell, fifthLevel, stackL, stackR, leadEnv, voiceL, voiceR);

        // --- kick ------------------------------------------------------------

        // Two envelopes and a sine, which is the whole of a kick drum: one
        // shapes how loud it is, the shorter one what pitch it is.
        var kickLevel = b.Add(NodeCatalog.AdsrTypeId, (1, -2.9f), (2, -0.62f), (3, 0f), (4, -1.1f));

        var kickSweep = b.Add(NodeCatalog.AdsrTypeId, (1, -3.3f), (2, -1.4f), (3, 0f), (4, -1.8f));

        var kickPitch = b.Add("math.remap", (1, 0f), (2, 1f), (3, 47f), (4, 205f));
        var kickBody = b.Add("osc.sine");
        var kickOut = b.Add("math.mul");

        b.Wire(kickSeq, 1, kickLevel, 0)
         .Wire(kickSeq, 1, kickSweep, 0)
         .Wire(kickSweep, 0, kickPitch, 0)
         .Wire(kickPitch, 0, kickBody, 1)
         .Wire(kickBody, 0, kickOut, 0)
         .Wire(kickLevel, 0, kickOut, 1);

        b.Group("Kick", kickLevel, kickSweep, kickPitch, kickBody, kickOut);

        // --- hats ------------------------------------------------------------

        // The hiss. Nothing in the catalogue makes a noise a point in the plane
        // can hear — see the remarks — so it is built: a large multiple of the
        // clock, a sine of it, a larger multiple of that, and the fraction.
        var grain = b.Add("math.mul", (1, 3571f));
        var hash = b.Add("math.sin");
        var scatter = b.Add("math.mul", (1, 4371.3f));
        var white = b.Add("math.fract");
        var hiss = b.Add("math.remap", (1, 0f), (2, 1f), (3, -1f), (4, 1f));

        // The step, as a decay time. That knob is in decades, so this is three
        // milliseconds at the bottom of the sequence and a seventh of a second
        // at the top of it.
        var hatOpen = b.Add("math.remap", (1, 0f), (2, 1f), (3, -2.5f), (4, -0.85f));
        var hatEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -3.7f), (3, 0f), (4, -2.2f));
        var hatOut = b.Add("math.mul");

        b.Wire(clock, 0, grain, 0)
         .Wire(grain, 0, hash, 0)
         .Wire(hash, 0, scatter, 0)
         .Wire(scatter, 0, white, 0)
         .Wire(white, 0, hiss, 0)
         .Wire(hatSeq, 0, hatOpen, 0)
         .Wire(hatOpen, 0, hatEnv, 2)
         .Wire(hatSeq, 1, hatEnv, 0)
         .Wire(hiss, 0, hatOut, 0)
         .Wire(hatEnv, 0, hatOut, 1);

        b.Group("Hats", grain, hash, scatter, white, hiss, hatOpen, hatEnv, hatOut);

        // --- the desk --------------------------------------------------------

        var deskL = b.Add("math.mixer", (1, 0.55f), (3, 0.72f), (5, 1f), (7, 0.55f));
        var deskR = b.Add("math.mixer", (1, 0.55f), (3, 0.72f), (5, 1f), (7, 0.8f));

        var driveL = b.Add("math.mul", (1, 1.2f));
        var driveR = b.Add("math.mul", (1, 1.2f));

        var limitL = b.Add("math.clamp", (1, -1f), (2, 1f));
        var limitR = b.Add("math.clamp", (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.62f));

        b.Wire(bassOut, 0, deskL, 0)
         .Wire(voiceL, 0, deskL, 2)
         .Wire(kickOut, 0, deskL, 4)
         .Wire(hatOut, 0, deskL, 6)

         .Wire(bassOut, 0, deskR, 0)
         .Wire(voiceR, 0, deskR, 2)
         .Wire(kickOut, 0, deskR, 4)
         .Wire(hatOut, 0, deskR, 6)

         .Wire(deskL, 0, driveL, 0)
         .Wire(deskR, 0, driveR, 0)
         .Wire(driveL, 0, limitL, 0)
         .Wire(driveR, 0, limitR, 0)
         .Wire(limitL, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(limitR, 0, output, NodeCatalog.OutputRightPort);

        b.Group("Desk", deskL, deskR, driveL, driveR, limitL, limitR);

        // --- the picture: geometry -------------------------------------------

        // One clock read at four speeds. They are Multiplies rather than four
        // Times for the reason Nebula gives: seconds are seconds, and what
        // differs between these is only how much of them each part wants.
        var spin = b.Add("math.mul", (1, 0.055f));
        var boil = b.Add("math.mul", (1, 0.18f));
        var drift = b.Add("math.mul", (1, 0.4f));
        var crawl = b.Add("math.mul", (1, 0.02f));

        // The bass moves the frame: where it has got to in the pattern is added
        // to the rotation and is how many wedges the fold has. It changes about
        // once a bar, which reads as an arrangement rather than as a fault.
        var stride = b.Add("math.remap", (1, 0f), (2, 1f), (3, -0.4f), (4, 0.4f));
        var angle = b.Add("math.add");
        var turn = b.Add("space.rotate");

        // The kick moves the light. Its gate is read directly rather than
        // through an envelope — see the remarks — and it is doing three things
        // at once: the zoom, the brightness, and the twist on the feedback.
        var pump = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.96f), (4, 1.3f));
        var zoom = b.Add("space.scale");

        var segments = b.Add("math.remap", (1, 0f), (2, 1f), (3, 3f), (4, 10f));
        var fold = b.Add("space.kaleidoscope");

        // Geometry alone looks like geometry, so the plane is bent by a field
        // read from inside the fold — symmetric, so it repeats with the wedges
        // rather than quietly undoing them.
        var field = b.Add("pattern.noise", (3, 2.1f));
        var breath = b.Add("osc.sine", (1, 0.071f), (3, 0.5f), (4, 0.5f));
        var reach = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.2f), (4, 0.7f));
        var bend = b.Add("space.warp");

        // The lead's gate widens the rings, so a sixteenth arrives as a band
        // rather than as a change of color alone.
        var count = b.Add("math.remap", (1, 0f), (2, 1f), (3, 2.6f), (4, 5.5f));
        var bands = b.Add("pattern.rings");

        // Rings are a sine, so most of the frame is dark and only the crests
        // survive as filaments.
        var filament = b.Add("math.smoothstep", (0, 0.2f), (1, 0.95f));

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(clock, 0, drift, 0)
         .Wire(clock, 0, crawl, 0)

         .Wire(bassSeq, 2, stride, 0)
         .Wire(spin, 0, angle, 0)
         .Wire(stride, 0, angle, 1)
         .Wire(angle, 0, turn, 2)

         .Wire(kickSeq, 1, pump, 0)
         .Wire(turn, 0, zoom, 0)
         .Wire(turn, 1, zoom, 1)
         .Wire(pump, 0, zoom, 2)

         .Wire(bassSeq, 2, segments, 0)
         .Wire(zoom, 0, fold, 0)
         .Wire(zoom, 1, fold, 1)
         .Wire(segments, 0, fold, 2)

         .Wire(fold, 0, field, 0)
         .Wire(fold, 1, field, 1)
         .Wire(boil, 0, field, 2)

         .Wire(breath, 0, reach, 0)
         .Wire(fold, 0, bend, 0)
         .Wire(fold, 1, bend, 1)
         .Wire(field, 0, bend, 2)
         .Wire(reach, 0, bend, 3)

         .Wire(leadSeq, 1, count, 0)
         .Wire(bend, 0, bands, 0)
         .Wire(bend, 1, bands, 1)
         .Wire(count, 0, bands, 2)
         .Wire(drift, 0, bands, 3)
         .Wire(bands, 0, filament, 2);

        b.Group("Picture: Geometry", spin, boil, drift, crawl, stride, angle, turn, pump,
            zoom, segments, fold, field, breath, reach, bend, count, bands, filament);

        // --- the picture: color ----------------------------------------------

        // The lead moves the hue, with the field and the slowest clock added
        // under it so a step is never quite the same color twice. Wrapped rather
        // than clamped, because a hue is a wheel.
        var stepped = b.Add("math.mul", (1, 0.8f));
        var wash = b.Add("math.mul", (1, 0.9f));
        var blend = b.Add("math.add");
        var slide = b.Add("math.add");
        var hue = b.Add("math.fract");

        // The bass's gate takes the color out of the image between its notes,
        // which is the same rhythm the ear is getting from it.
        var saturation = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.55f), (4, 0.95f));

        var glow = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.75f), (4, 1.7f));
        var lit = b.Add("math.mul");
        var visible = b.Add("math.clamp", (1, 0f), (2, 1f));

        var fresh = b.Add("color.hsv");

        b.Wire(leadSeq, 2, stepped, 0)
         .Wire(field, 0, wash, 0)
         .Wire(stepped, 0, blend, 0)
         .Wire(wash, 0, blend, 1)
         .Wire(blend, 0, slide, 0)
         .Wire(crawl, 0, slide, 1)
         .Wire(slide, 0, hue, 0)

         .Wire(bassSeq, 1, saturation, 0)

         .Wire(kickSeq, 1, glow, 0)
         .Wire(filament, 0, lit, 0)
         .Wire(glow, 0, lit, 1)
         .Wire(lit, 0, visible, 0)

         .Wire(hue, 0, fresh, 0)
         .Wire(saturation, 0, fresh, 1)
         .Wire(visible, 0, fresh, 2);

        b.Group("Picture: Color", stepped, wash, blend, slide, hue, saturation, glow, lit,
            visible, fresh);

        // --- the picture: feedback -------------------------------------------

        // Two readings of the last frame turning opposite ways, red from one and
        // green and blue from the other. That makes a chromatic tunnel, with no
        // lens anywhere in it.
        var inward = b.Add("space.scale", (2, 1.035f));
        var twist = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.012f), (4, 0.05f));
        var inTurn = b.Add("space.rotate");
        var pastIn = b.Add("feedback");
        var warm = b.Add("color.split");

        var outward = b.Add("space.scale", (2, 0.972f));
        var outTurn = b.Add("space.rotate", (2, -0.016f));
        var pastOut = b.Add("feedback");
        var cool = b.Add("color.split");

        var ghost = b.Add("color.rgb");
        var trail = b.Add("color.gain", (1, 0.85f), (2, 0f));

        // Max rather than a blend, for FeedbackTunnel's reason: a trail brighter
        // than the new frame keeps its brightness, which is what makes a streak
        // read as a streak rather than as a smeared copy.
        var combine = b.Add("math.max");

        b.Wire(kickSeq, 1, twist, 0)
         .Wire(inward, 0, inTurn, 0)
         .Wire(inward, 1, inTurn, 1)
         .Wire(twist, 0, inTurn, 2)
         .Wire(inTurn, 0, pastIn, 0)
         .Wire(inTurn, 1, pastIn, 1)
         .Wire(pastIn, 0, warm, 0)

         .Wire(outward, 0, outTurn, 0)
         .Wire(outward, 1, outTurn, 1)
         .Wire(outTurn, 0, pastOut, 0)
         .Wire(outTurn, 1, pastOut, 1)
         .Wire(pastOut, 0, cool, 0)

         .Wire(warm, 0, ghost, 0)
         .Wire(cool, 1, ghost, 1)
         .Wire(cool, 2, ghost, 2)
         .Wire(ghost, 0, trail, 0)

         .Wire(trail, 0, combine, 0)
         .Wire(fresh, 0, combine, 1)
         .Wire(combine, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Picture: Feedback", inward, twist, inTurn, pastIn, warm,
            outward, outTurn, pastOut, cool, ghost, trail, combine);

        return b.Build();
    }

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
    /// A tone, and a Scope drawing what the speakers actually played of it.
    /// </summary>
    public static Patch Waveform(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var pitch = b.Add("audio.frequency", (0, 160f));
        var tone = b.Add("osc.saw");

        // The tremolo, and the only thing in the patch that moves slowly enough
        // to be seen across a frame of the chart.
        var swell = b.Add("osc.sine", (1, 0.8f), (3, 0.45f), (4, 0.55f));
        var voice = b.Add("math.mul");

        // About twenty milliseconds, which holds a few cycles of the tremolo and
        // a great many of the tone. The knob is in decades — see
        // PortDisplay.Duration — so this is 10^-1.7.
        var chart = b.Add(NodeCatalog.ScopeTypeId, (1, -1.7f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, voice, 0)
         .Wire(swell, 0, voice, 1)
         .Wire(voice, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(voice, 0, chart, 0)
         .Wire(chart, 0, output, NodeCatalog.OutputColorPort);

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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.45f));

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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.7f));

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
    /// One Unit Delay closing a loop, which is a filter built by hand out of an
    /// add and a multiply.
    /// </summary>
    public static Patch Loop(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var pitch = b.Add("audio.frequency", (0, 110f));

        // The thing being filtered. A square, because its corners are what a
        // lowpass visibly and audibly takes off.
        var source = b.Add("osc.square");

        // Quiet going in, because the loop below has a great deal of gain in it:
        // what comes out is roughly the input divided by one minus the feedback.
        var quiet = b.Add("math.mul", (1, 0.06f));

        var sum = b.Add("math.add");
        var delay = b.Add(NodeCatalog.UnitDelayTypeId);

        // How much of the last evaluation is kept. Near one is a gentle filter,
        // and the useful range is all in the last hundredth — which is why it is
        // a knob of its own rather than a constant buried in the Multiply.
        var keep = b.Add("math.mul", (1, 0.94f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(pitch, 0, source, 1)
         .Wire(source, 0, quiet, 0)
         .Wire(quiet, 0, sum, 0)
         .Wire(keep, 0, sum, 1)
         .Wire(sum, 0, delay, 0)
         .Wire(delay, 0, keep, 0)
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

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.55f));

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
}
