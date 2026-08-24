namespace Flyback.Core.Graph;

/// <summary>
/// A patch to start from, and the name it appears under. Built on demand rather
/// than held ready, because a preset from a plugin can only be assembled once
/// that plugin's modules are in the catalogue.
/// </summary>
public sealed record PatchPreset(string Name, Func<ModuleCatalog, Patch> Build);

/// <summary>Patches that ship with the synth, so it never opens on a blank canvas.</summary>
public static class Presets
{
    public static IReadOnlyList<PatchPreset> All =>
    [
        new("Plasma", Plasma),
        new("Kaleidoscope", Kaleidoscope),
        new("Feedback tunnel", FeedbackTunnel),
        new("Nebula", Nebula),
        new("Drone", Drone),
        new("Ring scan", RingScan),
        new("Chromatic", Chromatic),
        new("Sequence", Sequence),
        new("Four voices", FourVoices),
        new("Kick", Kick),
        new("Empty", Empty),
    ];

    public static Patch Default() => Plasma(NodeCatalog.Current);

    /// <summary>
    /// One sequencer, heard and seen at once. The steps are the tune; where the
    /// sequence has got to is the color, and the gate that makes a rest silent
    /// is the same one that takes the light out of it.
    /// </summary>
    /// <remarks>
    /// Nothing here is duplicated between the two sinks. Every difference
    /// between what the ear gets and what the eye gets is a different output of
    /// the one module — which is the point of it having three.
    /// </remarks>
    public static Patch Sequence(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var time = b.Add("time", 40, 300);

        // Three notes a second, and the gate closed for the last third of each
        // so that two of the same note in a row are two notes. Ports are in,
        // rate, gate length, shape — the notes themselves are a list on the
        // node rather than knobs on it (ADR-0038).
        var steps = b.Add("seq.notes", 240, 180, (1, 3f), (2, 0.66f));

        // Ear: the step is a note number, so it goes in where a note goes.
        var note = b.Add("audio.note", 640, 180);
        var tone = b.Add("osc.sine", 840, 180);
        var voiced = b.Add("math.mul", 1040, 200);

        // Eye: rings whose count is the position in the pattern, so the picture
        // reorganises itself on the beat rather than drifting through it.
        var coord = b.Add("coord", 40, 620);
        // Remapped rather than multiplied, so the first step of the pattern is a
        // ring count of one and a half rather than of nothing: index starts at
        // zero, and zero rings is a flat field with no pattern in it at all.
        var depth = b.Add("math.remap", 400, 760, (1, 0f), (2, 1f), (3, 1.5f), (4, 9f));
        var rings = b.Add("pattern.rings", 640, 620);
        var glow = b.Add("math.remap", 840, 620, (1, -1f), (2, 1f), (3, 0.05f), (4, 1f));

        // The gate dims the picture exactly where it silences the tone, so the
        // rhythm is visible as well as audible — but only down to four tenths.
        // Multiplying by the gate itself is the obvious wiring and the wrong
        // one: the screen would be black for the third of every step that the
        // note is not sounding, which reads as a fault rather than as a pulse.
        var pulse = b.Add("math.remap", 1040, 860, (1, 0f), (2, 1f), (3, 0.4f), (4, 1f));
        var lit = b.Add("math.mul", 1040, 700);
        var color = b.Add("color.hsv", 1240, 620, (1, 0.8f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1440, 420, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(time, 0, steps, 0)
         .Wire(steps, 0, note, 0)
         .Wire(time, 0, tone, 0)
         .Wire(note, 0, tone, 1)
         .Wire(tone, 0, voiced, 0)
         .Wire(steps, 1, voiced, 1)
         .Wire(voiced, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(steps, 2, depth, 0)
         .Wire(depth, 0, rings, 2)
         .Wire(rings, 0, glow, 0)
         .Wire(steps, 1, pulse, 0)
         .Wire(glow, 0, lit, 0)
         .Wire(pulse, 0, lit, 1)
         .Wire(steps, 2, color, 0)
         .Wire(lit, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>
    /// A kick drum at 120 beats a minute, which is two envelopes and an
    /// oscillator: one shapes how loud it is and one shapes what pitch it is,
    /// and the second of those is the whole difference between a drum and a
    /// beep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A kick is a sine whose pitch falls out from under it. The pitch envelope
    /// is the short one — forty milliseconds, against three hundred for the
    /// level — so the note starts at two hundred hertz and has dropped to
    /// forty-five before it is half over. What the ear hears at the top is the
    /// beater and what it hears after is the shell, and both are one oscillator.
    /// </para>
    /// <para>
    /// The trigger is a Pulse rather than a sequencer, because every beat here is
    /// the same beat and a pattern of one repeated note is a list saying nothing.
    /// Its 'freq' is where the tempo lands: 120 beats a minute is two beats a
    /// second is two hertz, and the Tempo module is what lets that be typed as
    /// 120 rather than worked out. How long its gate is held barely matters:
    /// sustain is nothing, so the level has fallen to it long before the gate
    /// lets go, and what decides the length of a kick is the envelope.
    /// </para>
    /// <para>
    /// For the eye, the same level envelope lights a disc. The envelopes have no
    /// memory on the video path so what the screen actually gets is the gate —
    /// a flash a beat, in time with what the ear is hearing rather than shaped
    /// like it. Which is the honest picture of a drum: the rhythm is the part of
    /// it a still frame can carry.
    /// </para>
    /// </remarks>
    public static Patch Kick(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var time = b.Add("time", 40, 420);

        // 120 a minute, and the module that says so in those words.
        var tempo = b.Add(NodeCatalog.TempoTypeId, 40, 200, (0, 120f));

        // 'width' is how much of each beat the pulse is shut for, so a small one
        // opens just after the beat and stays open until just before the next.
        // Held far longer than the drum lasts, which is deliberate: what decides
        // how long a kick is, is the envelope and never the gate.
        var beat = b.Add("osc.pulse", 260, 200, (3, 0.02f));

        // Ear. Level and pitch, in that order down the column, and the pitch one
        // an order of magnitude shorter — see above.
        var level = b.Add(NodeCatalog.AdsrTypeId, 520, 200,
            (1, -2.7f), (2, -0.52f), (3, 0f), (4, -1.3f));

        var sweep = b.Add(NodeCatalog.AdsrTypeId, 520, 480,
            (1, -3f), (2, -1.35f), (3, 0f), (4, -2f));

        // The envelope is nought to one and a pitch is in hertz, so it is
        // remapped rather than multiplied: the bottom of the sweep is where the
        // drum sits and the top is how far above it the beater is.
        var pitch = b.Add("math.remap", 780, 480, (1, 0f), (2, 1f), (3, 45f), (4, 200f));

        var body = b.Add("osc.sine", 1020, 480);
        var struck = b.Add("math.mul", 1260, 300);

        // Eye. A disc, brightest in the middle and gone by the edge of the frame.
        var coord = b.Add("coord", 40, 760);
        var disc = b.Add("math.remap", 300, 760, (1, 0f), (2, 0.9f), (3, 1f), (4, 0f));

        // What lights it is a saw at the tempo and not the level envelope, which
        // cannot help here: an envelope has no memory on the video path, so all
        // it can hand over is its gate — and that gate is open for all but a
        // fiftieth of each beat. A disc lit by it is not a drum being struck but
        // a lamp that is on, with a ten-millisecond gap in it that a frame is too
        // long to catch reliably. What you see then is a light blinking at
        // whatever rate the two happen to beat against each other.
        //
        // A saw is the shape the envelope would be if it could run: it resets on
        // the beat and falls away, and being a pure function of time it needs no
        // memory to do it. Its 'phase' puts the reset where the Pulse's edge is,
        // so the flash and the strike land together.
        var fall = b.Add("osc.saw", 300, 980, (2, 0.98f));

        // Bright on the beat, down to nothing two thirds of the way to the next
        // — the same share of a beat the level envelope's decay takes, so what
        // the eye is given is the length of the drum rather than of the gate.
        var shape = b.Add("math.remap", 540, 980, (1, -1f), (2, 1f), (3, 1f), (4, -0.5f));
        var visible = b.Add("math.clamp", 780, 980, (1, 0f), (2, 1f));

        var flash = b.Add("math.mul", 1020, 860);
        var skin = b.Add("color.hsv", 1260, 760, (0, 0.04f), (1, 0.85f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1500, 420, (NodeCatalog.OutputGainPort, 0.8f));

        b.Wire(tempo, 0, beat, 1)
         .Wire(time, 0, beat, 0)
         .Wire(beat, 0, level, 0)
         .Wire(beat, 0, sweep, 0)
         .Wire(sweep, 0, pitch, 0)
         .Wire(time, 0, body, 0)
         .Wire(pitch, 0, body, 1)
         .Wire(body, 0, struck, 0)
         .Wire(level, 0, struck, 1)
         .Wire(struck, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(coord, 2, disc, 0)
         .Wire(time, 0, fall, 0)
         .Wire(tempo, 0, fall, 1)
         .Wire(fall, 0, shape, 0)
         .Wire(shape, 0, visible, 0)
         .Wire(disc, 0, flash, 0)
         .Wire(visible, 0, flash, 1)
         .Wire(flash, 0, skin, 2)
         .Wire(skin, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>Just the Output, with everything still to plug into it.</summary>
    public static Patch Empty(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);
        b.Add(NodeCatalog.OutputTypeId, 640, 260);
        return b.Patch;
    }

    /// <summary>Two sine fields crossed and read as hue — the "hello world" of video synths.</summary>
    public static Patch Plasma(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 200);
        var time = b.Add("time", 40, 400);

        // A fifth of a radian a second into the phase below. Time is seconds and
        // nothing else, so a patch that wants less than that says so here.
        var slowly = b.Add("math.mul", 150, 400, (1, 0.2f));

        // Sine along x, and a second along y whose phase drifts with time.
        var horizontal = b.Add("osc.sine", 260, 120, (1, 1.5f));
        var vertical = b.Add("osc.sine", 260, 300, (1, 1.1f));

        var sum = b.Add("math.add", 500, 200);
        var hue = b.Add("math.remap", 660, 200, (1, -2f), (2, 2f), (3, 0f), (4, 1f));
        var color = b.Add("color.hsv", 860, 200, (1, 0.85f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1060, 220);

        b.Wire(coord, 0, horizontal, 0)
         .Wire(coord, 1, vertical, 0)
         .Wire(time, 0, slowly, 0)
         .Wire(slowly, 0, vertical, 2)
         .Wire(horizontal, 0, sum, 0)
         .Wire(vertical, 0, sum, 1)
         .Wire(sum, 0, hue, 0)
         .Wire(hue, 0, color, 0)
         .Wire(color, 0, output, 0);

        return b.Patch;
    }

    /// <summary>Rotating wedges filled with noise that boils over time.</summary>
    public static Patch Kaleidoscope(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 220);

        // One clock and two speeds off it, rather than two clocks. An output
        // fans out to as many inputs as you like, so what a patch needs more
        // than one of is the scaling, not the time.
        var clock = b.Add("time", 40, 260);
        var spin = b.Add("math.mul", 150, 60, (1, 0.15f));
        var drift = b.Add("math.mul", 150, 460, (1, 0.3f));

        var rotate = b.Add("space.rotate", 260, 160);
        var fold = b.Add("space.kaleidoscope", 470, 200, (2, 6f));
        var noise = b.Add("pattern.noise", 680, 240, (3, 2.5f));
        var color = b.Add("color.hsv", 890, 240, (1, 0.9f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1090, 260);

        b.Wire(coord, 0, rotate, 0)
         .Wire(coord, 1, rotate, 1)
         .Wire(clock, 0, spin, 0)
         .Wire(clock, 0, drift, 0)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, fold, 0)
         .Wire(rotate, 1, fold, 1)
         .Wire(fold, 0, noise, 0)
         .Wire(fold, 1, noise, 1)
         .Wire(drift, 0, noise, 2)
         .Wire(noise, 0, color, 0)
         .Wire(color, 0, output, 0);

        return b.Patch;
    }

    /// <summary>
    /// One patch heard and seen at once. A single slow oscillator sets both the
    /// hue of the image and the tremolo on the tone, so the two sinks are
    /// visibly and audibly the same signal. Switch Audio on to hear it.
    /// </summary>
    public static Patch Drone(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 120);
        var time = b.Add("time", 40, 380);

        // The shared control signal, remapped to 0..1 by amp and bias.
        var slow = b.Add("osc.sine", 260, 340, (1, 0.15f), (3, 0.5f), (4, 0.5f));

        // Ear.
        var pitch = b.Add("audio.frequency", 260, 560, (0, 110f));
        var tone = b.Add("osc.sine", 470, 560, (1, 110f));
        var tremolo = b.Add("math.mul", 700, 580);

        // Eye.
        var rings = b.Add("pattern.rings", 260, 80, (2, 3f));
        var tint = b.Add("color.hsv", 700, 140, (1, 0.85f));

        // Both halves land on the one block, which is what makes the shared
        // oscillator legible: two wires into the same module, from the same sine.
        var output = b.Add(NodeCatalog.OutputTypeId, 920, 340, (NodeCatalog.OutputGainPort, 0.6f));

        b.Wire(time, 0, slow, 0)
         .Wire(time, 0, tone, 0)
         .Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, tremolo, 0)
         .Wire(slow, 0, tremolo, 1)
         .Wire(tremolo, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(time, 0, rings, 3)
         .Wire(slow, 0, tint, 0)
         .Wire(rings, 0, tint, 2)
         .Wire(tint, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>
    /// The picture played rather than drawn. A circle is swept round a field of
    /// rings at audio rate and what it passes over is the waveform, so the tone
    /// is not made by an oscillator anywhere — it is the image, read along a
    /// line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rings are centred on the origin and the loop is not, which is the
    /// whole of why this makes a sound. Distance from the origin is what Rings
    /// is a function of, so a loop centred there sits on one ring for the entire
    /// turn and reads a constant; pushed off centre it crosses several, and the
    /// crossing is the waveform. Slide the Scan's 'x' back to nothing and the
    /// patch goes silent with the picture unchanged, which is the fastest way to
    /// see what the module is actually doing.
    /// </para>
    /// <para>
    /// What it is doing is FM. The value along the loop is the sine of a
    /// distance that varies smoothly round the turn, and a sine of a periodic
    /// function is a phase-modulated one — so the Rings' own 'freq' is the
    /// modulation index and winding it up blooms the harmonics rather than
    /// changing the pitch. The pitch is the Scan's 'rate' and nothing else.
    /// </para>
    /// <para>
    /// The slow sine walks the loop's centre outward and back, which is a
    /// wavetable sweep: the table is the field, and where the loop is cut
    /// through it is the position in the table. That is the knob this patch is
    /// really for. The Scan's 'view' is laid over the rings so the loop can be
    /// seen where it runs, with the value it is reading swinging the trace off
    /// it — the X-Y display to a Probe's chart.
    /// </para>
    /// <para>
    /// The rings are lowered twice here, once for the eye and once inside the
    /// sweep (ADR-0040): the two are the same module read at different places,
    /// and two readings cannot share a register. It costs ops rather than
    /// correctness, and it is the price of seeing the thing being scanned.
    /// </para>
    /// </remarks>
    public static Patch RingScan(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var time = b.Add("time", 40, 420);
        var coord = b.Add("coord", 40, 120);

        // The field, and the only thing in the patch that makes the waveform.
        // 'freq' is the modulation index rather than a pitch: more rings under
        // the loop is more harmonics, at the same note.
        var rings = b.Add("pattern.rings", 300, 120, (2, 4f));

        // Where the loop is cut through the field, walked outward and back twice
        // a second. Kept clear of zero at the bottom of the sweep, because a loop
        // concentric with the rings reads a constant and is silent.
        var sweep = b.Add("osc.sine", 300, 600, (1, 0.2f));
        var where = b.Add("math.remap", 540, 640, (1, -1f), (2, 1f), (3, 0.2f), (4, 0.75f));

        var pitch = b.Add("audio.frequency", 300, 780, (0, 110f));

        // 'clock' is the sweep's own time base; 'rate' is the pitch; 'radius'
        // and 'x' choose which loop through the field is read.
        var scan = b.Add(NodeCatalog.ScanTypeId, 800, 420, (3, 0.35f), (6, 1f));

        // Eye: the field under the trace, dim enough that the loop reads on top
        // of it rather than competing with it.
        var glow = b.Add("math.remap", 540, 120, (1, -1f), (2, 1f), (3, 0.05f), (4, 0.55f));
        var tint = b.Add("color.hsv", 800, 120, (1, 0.7f));
        var lit = b.Add("math.add", 1080, 220);

        var output = b.Add(NodeCatalog.OutputTypeId, 1320, 300, (NodeCatalog.OutputGainPort, 0.45f));

        b.Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)

         // Ear: the field itself into the sweep, and out the other side as a
         // sample. Nothing between the picture and the speakers but the loop.
         .Wire(rings, 0, scan, 0)
         .Wire(time, 0, scan, 1)
         .Wire(pitch, 0, scan, 2)
         .Wire(time, 0, sweep, 0)
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

        return b.Patch;
    }

    /// <summary>
    /// The Note module on both sinks at once: one ramp, snapped to whole notes,
    /// heard as a chromatic run and seen as the steps it climbs through.
    /// </summary>
    /// <remarks>
    /// The ramp is smooth and nothing downstream of the snap is. That is the
    /// whole demonstration, and it is why the same signal drives both sinks: the
    /// ear hears a run of separate notes rather than a slide, and the eye sees
    /// flat rings of color rather than a gradient. Feed the ramp straight to a
    /// Frequency knob instead and both become continuous.
    /// <para>
    /// The steps are instant and the tone is clean, which sounds like a
    /// contradiction and is the point of ADR-0030: the sine accumulates its
    /// phase, so a frequency that jumps three times a second changes how fast
    /// the wave is travelling without moving where it currently is. Nothing in
    /// the pitch is smoothed. There is nothing to smooth.
    /// </para>
    /// <para>
    /// The picture reads the ramp at every radius at once, which is the trick
    /// worth stealing: the audio path pins x and y to zero, so the same Note
    /// module the speakers hear one note from is showing the eye the fourteen
    /// either side of it.
    /// </para>
    /// </remarks>
    public static Patch Chromatic(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 320);
        var time = b.Add("time", 40, 100);

        // Half an octave either way, over four seconds: six semitones up and
        // six down from the note on the knob, then it starts again. The reset is
        // a jump of a whole octave and it costs nothing, for the same reason the
        // semitones do.
        var ramp = b.Add("osc.saw", 250, 100, (1, 0.25f), (3, 0.5f));

        // Distance from the centre on the same scale, subtracted rather than
        // added so the rings travel outward as the ramp climbs.
        var spread = b.Add("math.mul", 250, 340, (1, 0.6f));
        var sweep = b.Add("math.sub", 460, 200);

        // A3 on the knob, and whatever arrives snapped to the nearest semitone.
        var note = b.Add("audio.note", 660, 220);

        // Ear.
        var tone = b.Add("osc.sine", 880, 460);

        // Eye: one hue per semitone, wrapped so an octave is a full turn of the
        // wheel and the same note always comes out the same color.
        var wheel = b.Add("math.mul", 880, 100, (1, 1f / 12f));
        var hue = b.Add("math.fract", 1080, 100);

        // Brightness is taken from the ramp itself rather than from the note, so
        // the two are in one picture: a smooth glow with hard-edged color rings
        // sitting in it, which is the before and after of the snap.
        var glow = b.Add("math.remap", 1080, 300, (1, -1.8f), (2, 0.5f), (3, 0.08f), (4, 1f));

        var color = b.Add("color.hsv", 1270, 140, (1, 0.85f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1470, 300, (NodeCatalog.OutputGainPort, 0.5f));

        b.Wire(time, 0, ramp, 0)
         .Wire(coord, 2, spread, 0)
         .Wire(ramp, 0, sweep, 0)
         .Wire(spread, 0, sweep, 1)
         .Wire(sweep, 0, note, 1)
         .Wire(time, 0, tone, 0)
         .Wire(note, 0, tone, 1)
         .Wire(tone, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(note, 1, wheel, 0)
         .Wire(wheel, 0, hue, 0)
         .Wire(sweep, 0, glow, 0)
         .Wire(hue, 0, color, 0)
         .Wire(glow, 0, color, 2)
         .Wire(color, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>
    /// Everything the video side can do, in one patch: coordinates turned, folded
    /// into wedges, bent by a noise field read from inside the fold, taken as
    /// travelling rings, and laid over a trail of its own previous frames.
    /// </summary>
    /// <remarks>
    /// The step that matters is the Warp. Rotate, Kaleidoscope and Rings are all
    /// geometry, and geometry alone looks like geometry however much of it you
    /// stack up — the picture only stops looking constructed once the coordinates
    /// themselves are displaced by something with no structure.
    /// <para>
    /// The order of those two is the whole trick, and it is not the obvious one.
    /// Warping before folding looks marvellous and is not a kaleidoscope at all:
    /// the displacement varies per pixel, so the fold has nothing symmetric left
    /// to work with and the eight-fold structure vanishes. Folding first and
    /// warping inside the wedge — by a field that is itself read from the folded
    /// plane, so it repeats with it — keeps the symmetry and bends it at once.
    /// </para>
    /// <para>
    /// Feeding that same field into the hue is what ties shape to color, so it
    /// reads as one moving thing rather than a pattern with a palette applied to
    /// it. And it is the most expensive preset here by some way, which is also
    /// the point of having it: it is what the renderer looks like under load.
    /// </para>
    /// </remarks>
    public static Patch Nebula(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 300);

        // One clock read at three speeds, so nothing in the picture ever quite
        // lines up with anything else and it does not visibly loop. The three
        // are Multiplies rather than three Times: seconds are seconds, and what
        // differs between these is only how much of them each part wants.
        var clock = b.Add("time", 40, 400);
        var spin = b.Add("math.mul", 150, 80, (1, 0.05f));
        var boil = b.Add("math.mul", 150, 560, (1, 0.12f));
        var pulse = b.Add("math.mul", 150, 720, (1, 0.2f));

        var turn = b.Add("space.rotate", 250, 220);
        var fold = b.Add("space.kaleidoscope", 450, 220, (2, 8f));

        // Read from the folded plane, not the flat one, so the field is itself
        // symmetric — warping by anything asymmetric here would quietly undo the
        // fold and leave the picture looking like ordinary noise.
        var field = b.Add("pattern.noise", 660, 560, (3, 1.4f));

        var bend = b.Add("space.warp", 880, 240, (3, 0.5f));
        var bands = b.Add("pattern.rings", 1080, 260, (2, 2.5f));

        // Rings are a sine, so most of the frame is dark and only the crests
        // survive as filaments.
        var filament = b.Add("math.smoothstep", 1080, 320, (0, 0.15f), (1, 0.85f));

        // Hue drifts with the field and with time, wrapped back into 0..1.
        var drift = b.Add("math.add", 890, 620);
        var hue = b.Add("math.fract", 1080, 620);

        var fresh = b.Add("color.hsv", 1270, 400, (1, 0.85f));

        // The previous frame, zoomed out a hair and turned, so what is already on
        // screen spirals outward while new filaments arrive underneath it.
        var widen = b.Add("space.scale", 250, 900, (2, 0.99f));
        var swirl = b.Add("space.rotate", 450, 900, (2, 0.015f));
        var previous = b.Add("feedback", 660, 900);
        var trail = b.Add("color.gain", 870, 900, (1, 0.92f), (2, 0f));

        // Max rather than a blend: a trail that is brighter than the new frame
        // keeps its brightness, which is what makes the streaks read as trails
        // rather than as a smeared copy.
        var combine = b.Add("math.max", 1470, 620);
        var output = b.Add(NodeCatalog.OutputTypeId, 1660, 640);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(clock, 0, pulse, 0)
         .Wire(coord, 0, turn, 0)
         .Wire(coord, 1, turn, 1)
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
         .Wire(coord, 0, widen, 0)
         .Wire(coord, 1, widen, 1)
         .Wire(widen, 0, swirl, 0)
         .Wire(widen, 1, swirl, 1)
         .Wire(swirl, 0, previous, 0)
         .Wire(swirl, 1, previous, 1)
         .Wire(previous, 0, trail, 0)
         .Wire(trail, 0, combine, 0)
         .Wire(fresh, 0, combine, 1)
         .Wire(combine, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>
    /// The camera-pointed-at-its-own-monitor patch: each frame is re-read
    /// slightly rotated, scaled and dimmed, with fresh rings fed in on top.
    /// </summary>
    public static Patch FeedbackTunnel(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 240);

        var clock = b.Add("time", 40, 300);
        var spin = b.Add("math.mul", 150, 60, (1, 0.08f));
        var pulse = b.Add("math.mul", 150, 560, (1, 0.25f));

        var rotate = b.Add("space.rotate", 250, 140);
        var scale = b.Add("space.scale", 440, 160, (2, 1.05f));
        var previous = b.Add("feedback", 620, 180);
        var dim = b.Add("color.gain", 790, 180, (1, 0.95f), (2, 0f));

        // Fresh material: bright rings that travel outward.
        var rings = b.Add("pattern.rings", 250, 460, (2, 1.5f));
        var spark = b.Add("math.smoothstep", 450, 500, (0, 0.8f), (1, 1f));
        var tint = b.Add("color.hsv", 640, 520, (1, 1f));

        var combine = b.Add("math.max", 950, 300);
        var output = b.Add(NodeCatalog.OutputTypeId, 1130, 320);

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, pulse, 0)
         .Wire(coord, 0, rotate, 0)
         .Wire(coord, 1, rotate, 1)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, scale, 0)
         .Wire(rotate, 1, scale, 1)
         .Wire(scale, 0, previous, 0)
         .Wire(scale, 1, previous, 1)
         .Wire(previous, 0, dim, 0)
         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(pulse, 0, rings, 3)
         .Wire(rings, 0, spark, 2)
         .Wire(pulse, 0, tint, 0)
         .Wire(spark, 0, tint, 2)
         .Wire(dim, 0, combine, 0)
         .Wire(tint, 0, combine, 1)
         .Wire(combine, 0, output, 0);

        return b.Patch;
    }

    /// <summary>
    /// Four voices, each with a fader, through one Mixer at each sink. The
    /// faders are the shared signal: level three on the chord is level three on
    /// the screen, so what fades up in the sound is the same thing that fades up
    /// in the picture.
    /// </summary>
    /// <remarks>
    /// The patch is laid out as four channel strips, one voice a row, because
    /// that is what it is. Each row is a note and a sine at it for the ear, the
    /// same oscillator read across the screen instead of across time for the
    /// eye, and one slow sine setting both of their levels — which is the whole
    /// point of a level being a socket rather than only a knob.
    /// <para>
    /// Two Mixers rather than one, because the two sinks carry different things
    /// — and the same module twice, because its sockets are untyped: the chord
    /// sums four scalars and the picture sums four colors, by the same four
    /// multiplies and three adds.
    /// </para>
    /// <para>
    /// Both sinks pull the sum back down, and not by the same amount. A mixer
    /// sums rather than averages, so four voices at full are four times over —
    /// the Output's gain is a quarter, which is exactly that, and puts the worst
    /// case at full scale rather than past it. The picture's Gain is a good deal
    /// more generous, because the two sinks fail differently: light that runs
    /// over clips to white and reads as brightness, and sound that runs over
    /// clips to distortion and reads as a fault.
    /// </para>
    /// </remarks>
    public static Patch FourVoices(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var time = b.Add("time", 40, 300);
        var coord = b.Add("coord", 40, 480);

        var chord = b.Add("math.mixer", 1380, 60);
        var picture = b.Add("math.mixer", 1380, 320);

        var tame = b.Add("color.gain", 1620, 400, (1, 0.6f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1840, 180, (NodeCatalog.OutputGainPort, 0.25f));

        b.Wire(chord, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(picture, 0, tame, 0)
         .Wire(tame, 0, output, NodeCatalog.OutputColorPort);

        // An A major triad spread over two octaves, one voice a note: the root,
        // the fifth, the octave and the third above it. The band count climbs
        // with the voice, so how many rings are on screen is which note is
        // sounding — and the hues sit far enough apart that two voices at once
        // read as a third color rather than as a brighter one of the first.
        //
        // The fader rates share no common factor worth the name, so the four
        // never come up together twice and the patch does not visibly loop.
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
            var row = 60 + v * 180;

            // The fader. Amp and bias put a sine's -1..1 into the 0..1 a level
            // is edited within, so it opens and closes rather than going through
            // zero and coming back the other way up.
            var level = b.Add("osc.sine", 280, row, (1, rate), (2, phase), (3, 0.5f), (4, 0.5f));

            // Ear: the note, and a sine at it.
            var pitch = b.Add("audio.note", 500, row, (0, note));
            var tone = b.Add("osc.sine", 700, row);

            // Eye: the same oscillator run over the radius instead of over the
            // clock, so it stands still as bands out from the centre rather than
            // travelling as a tone.
            var band = b.Add("osc.sine", 940, row, (1, bands), (3, 0.5f), (4, 0.5f));
            var tint = b.Add("color.hsv", 1160, row, (0, hue));

            // Channel v of both mixers: the input, then the level beside it.
            var channel = v * 2;

            b.Wire(time, 0, level, 0)
             .Wire(time, 0, tone, 0)
             .Wire(pitch, 0, tone, 1)
             .Wire(tone, 0, chord, channel)
             .Wire(level, 0, chord, channel + 1)

             .Wire(coord, 2, band, 0)
             .Wire(band, 0, tint, 2)
             .Wire(tint, 0, picture, channel)
             .Wire(level, 0, picture, channel + 1);
        }

        return b.Patch;
    }
}
