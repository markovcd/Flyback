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
        new("In key", InKey),
        new("Sequence", Sequence),
        new("Four voices", FourVoices),
        new("Kick", Kick),
        new("Heard", Heard),
        new("Played", Played),
        new("Whole band", WholeBand),
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
    /// <para>
    /// There is no clock in it and no Coordinates either. The sequencer's and
    /// the oscillator's <c>in</c> are normalled to Time and the Rings' <c>x</c>
    /// and <c>y</c> to Coordinates (ADR-0050), so both are already driven and
    /// the whole patch is the part somebody chose.
    /// </para>
    /// </remarks>
    public static Patch Sequence(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Three notes a second, and the gate closed for the last third of each
        // so that two of the same note in a row are two notes. Ports are in,
        // rate, gate length, shape — the notes themselves are a list on the
        // node rather than knobs on it (ADR-0038). 'in' takes no wire: it runs
        // on the clock every domain socket is normalled to.
        var steps = b.Add("seq.notes", 240, 180, (1, 3f), (2, 0.66f));

        // Ear: the step is a note number, so it goes in where a note goes.
        var note = b.Add("audio.note", 640, 180);
        var tone = b.Add("osc.sine", 840, 180);
        var voiced = b.Add("math.mul", 1040, 200);

        // Eye: rings whose count is the position in the pattern, so the picture
        // reorganises itself on the beat rather than drifting through it.
        //
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
    /// <summary>
    /// The same drum as <see cref="Kick"/>, with the picture listening to it
    /// rather than being told about the beat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Kick's own note says what this is here to answer: its envelopes have
    /// no memory on the video path, so what the screen gets of a drum is the
    /// gate — a flash a beat, in time with what the ear hears rather than shaped
    /// like it. Every patch before this one that wanted the eye and the ear to
    /// agree did it the same way, by sending one modulator to both, which is a
    /// patch saying a thing twice rather than one half of it hearing the other.
    /// </para>
    /// <para>
    /// Here nothing is sent to both. The picture is driven by a Meter, and a
    /// Meter is not computed by the picture at all: it is a reading of what the
    /// speakers actually played, handed to the frame the way a note somebody is
    /// holding down is handed to it. So what lights the rings is the envelope
    /// itself — the fall of it as well as the start — and the color leans with
    /// the loudness rather than snapping with the trigger.
    /// </para>
    /// <para>
    /// Turn the sound off and the picture stops moving — it does not go out,
    /// because a floor under the reading keeps the field dimly lit, but nothing
    /// in it changes. That is the honest statement of what the module is and the
    /// one thing to know before building on it: there is no level without a
    /// speaker. Both readings are used, because the difference between them is
    /// most of the point: 'peak' is the hit and lights the rings, 'level' is the
    /// loudness of the window and takes the hue, so the color lags the flash by
    /// exactly as much as a room does.
    /// </para>
    /// <para>
    /// The window is a thirtieth of a second, which is about a frame. Shorter
    /// than that and the picture is sampling a slice of each frame and reads as
    /// a flicker; much longer and a drum this short is diluted into the silence
    /// around it. It is the only smoothing there is, which is why it is the knob
    /// to reach for first.
    /// </para>
    /// </remarks>
    public static Patch Heard(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // Two beats a second, and a gate short enough that the envelope decides
        // how long the drum is rather than the trigger — the same arrangement
        // the Kick uses and for the same reason.
        var beat = b.Add("osc.pulse", 80, 300, (1, 2f), (3, 0.08f));

        var level = b.Add(NodeCatalog.AdsrTypeId, 320, 300, (1, -2.6f), (2, -0.9f), (3, 0f), (4, -1f));

        var pitch = b.Add("audio.frequency", 80, 560, (0, 70f));
        var tone = b.Add("osc.sine", 320, 560);
        var voiced = b.Add("math.mul", 580, 420);

        // The one wire that is new in the machine: a signal on its way to the
        // speakers, read by something that hands the picture a number for it.
        // 'in' is swept, so nothing upstream of here is lowered into the frame —
        // the drum is not computed per pixel to be looked at.
        var heard = b.Add(NodeCatalog.MeterTypeId, 840, 420, (1, -1.5f));

        var rings = b.Add("pattern.rings", 840, 120, (2, 5f));
        var glow = b.Add("math.remap", 1080, 120, (1, -1f), (2, 1f), (3, 0.1f), (4, 1f));

        // A floor under the reading, for the reason the Sequence preset puts one
        // under its gate: a picture that is black whenever nothing is sounding
        // reads as a fault rather than as a pulse — and with the sound switched
        // off altogether it would be a preset that draws nothing at all.
        var swell = b.Add("math.add", 1080, 420, (1, 0.18f));
        var lit = b.Add("math.mul", 1320, 200);
        var color = b.Add("color.hsv", 1560, 240, (1, 0.75f));

        var output = b.Add(
            NodeCatalog.OutputTypeId, 1800, 420, (NodeCatalog.OutputGainPort, 0.6f));

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

        return b.Patch;
    }

    public static Patch Kick(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

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
         .Wire(beat, 0, level, 0)
         .Wire(beat, 0, sweep, 0)
         .Wire(sweep, 0, pitch, 0)
         .Wire(pitch, 0, body, 1)
         .Wire(body, 0, struck, 0)
         .Wire(level, 0, struck, 1)
         .Wire(struck, 0, output, NodeCatalog.OutputLeftPort)

         .Wire(coord, 2, disc, 0)
         .Wire(tempo, 0, fall, 1)
         .Wire(fall, 0, shape, 0)
         .Wire(shape, 0, visible, 0)
         .Wire(disc, 0, flash, 0)
         .Wire(visible, 0, flash, 1)
         .Wire(flash, 0, skin, 2)
         .Wire(skin, 0, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>
    /// The one preset you have to play. Nothing in it moves on its own: a MIDI
    /// In drives the pitch, the envelope and the timbre, and with no key down it
    /// is silent and the picture is dim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All three of the module's useful outputs are here, and each is wired the
    /// way only it can be. 'pitch' is a note number, so it goes where a note goes
    /// — into a Note for the ear, and through a Clamp into a pair of Remaps for
    /// the eye, where it picks both the hue and how tight the rings are. 'gate'
    /// opens the envelope.
    /// 'trigger' is the one that needs the least obvious wiring and earns its
    /// place: it is a single evaluation high at each note struck, which is
    /// exactly what a Sample &amp; Hold's own trigger wants.
    /// </para>
    /// <para>
    /// What the hold catches is a Noise wandering on the clock, and what it does
    /// with it is set the Pulse's duty cycle — so every note struck has a timbre
    /// of its own, settled the instant it starts and steady for as long as it is
    /// held. Sampling a moving signal is the whole point of the module; doing it
    /// on a note rather than on a beat is what a keyboard adds to it. Without the
    /// hold, the same Noise straight into 'width' would smear the tone about
    /// while a note was sounding, which is a different and much less musical
    /// instrument.
    /// </para>
    /// <para>
    /// Velocity is deliberately not wired. A typist strikes every key the same,
    /// so a patch that shipped with it wired would be a patch with a knob that
    /// does nothing until hardware arrives — worse than one with an obvious
    /// place to add a wire.
    /// </para>
    /// <para>
    /// The trigger reaches the ear and not the eye, and that is not an oversight.
    /// A picture is one evaluation with nothing before it, so there is no
    /// previous count for the module to have differenced — see ADR-0056 — and a
    /// trigger on the screen is nought at every pixel by decision rather than by
    /// accident. The Sample &amp; Hold is in the same position and stops holding
    /// there for the same reason. So the eye is given the two outputs that mean
    /// something without a past: which note, and whether one is down.
    /// </para>
    /// <para>
    /// The screen dims rather than going black between notes, exactly as
    /// <see cref="Sequence"/>'s does and for the same reason: a patch that is
    /// black until you touch it reads as one that is broken.
    /// </para>
    /// </remarks>
    public static Patch Played(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // No knobs on it at all. Which keyboard it listens to is the one thing it
        // carries, and a fresh one carries the computer's own.
        var keys = b.Add(NodeCatalog.MidiTypeId, 40, 420);
        keys.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiExtra.IndexField] = 1f,
        });

        // Ear. The note number goes in where a note number goes, and comes out
        // as hertz.
        var note = b.Add("audio.note", 360, 120);

        // A pulse rather than a saw, because its width is somewhere for the
        // held value to go. 'in' takes no wire: it runs on the clock every
        // domain socket is normalled to (ADR-0050).
        var tone = b.Add("osc.pulse", 1120, 120);

        // A pluck: quick on, most of the way down in a fifth of a second, and
        // held at half while the key is. The times are decades of seconds — see
        // PortDisplay.Duration — so -2.4 is about four milliseconds.
        var env = b.Add(NodeCatalog.AdsrTypeId, 360, 420,
            (1, -2.4f), (2, -0.7f), (3, 0.5f), (4, -1f));

        var voiced = b.Add("math.mul", 1400, 220);

        // The timbre, which is the whole of what 'trigger' is here for. A clock
        // into 'z' is what makes the field wander rather than sit still; x and y
        // are nothing at the speakers, so what the ear's copy of this walks is a
        // line through the noise rather than a picture of it.
        var clock = b.Add("time", 360, 700);
        var drift = b.Add("math.mul", 560, 700, (1, 3f));
        var wander = b.Add("pattern.noise", 760, 700);
        var caught = b.Add(NodeCatalog.HoldTypeId, 960, 700);

        // Never all the way to either end: a duty cycle of nought or one is
        // silence, and a note that happened to catch one would simply not sound.
        var width = b.Add("math.remap", 1160, 700, (1, 0f), (2, 1f), (3, 0.12f), (4, 0.88f));

        // Eye. Two readings of the same note number — what color it is, and how
        // finely the rings are drawn — over the two octaves either side of
        // middle C, which is wider than the two rows of a typewriter reach and
        // leaves room for a keyboard that reaches further.
        //
        // Held into that range before either reading, which does two things at
        // once. An eighty-eight-key keyboard runs past both ends of it, and past
        // the ends the hue would wrap round to a color the other end is already
        // using. And it settles what an unplayed patch looks like: nobody playing
        // reads as nought — the same answer a program with no block at all gives
        // — and nought is not a note anybody will strike, so the picture rests at
        // the bottom of the range it draws rather than wherever nought lands.
        var range = b.Add("math.clamp", 360, 1080, (1, 36f), (2, 84f));

        var hue = b.Add("math.remap", 620, 1000, (1, 36f), (2, 84f), (3, 0.55f), (4, 0f));
        var fineness = b.Add("math.remap", 620, 1160, (1, 36f), (2, 84f), (3, 2f), (4, 11f));

        var rings = b.Add("pattern.rings", 900, 1080);
        var glow = b.Add("math.remap", 1160, 1080, (1, -1f), (2, 1f), (3, 0.1f), (4, 1f));

        // What the envelope hands the screen is its gate, since an envelope has
        // no memory to run a shape in on the video path. Held rather than struck,
        // that is the right picture of a keyboard: the light is on while the key
        // is down. Dimmed to a quarter rather than to nothing between notes.
        var lift = b.Add("math.remap", 1160, 1300, (1, 0f), (2, 1f), (3, 0.25f), (4, 1f));
        var lit = b.Add("math.mul", 1400, 1080);
        var skin = b.Add("color.hsv", 1660, 1000, (1, 0.8f));

        var output = b.Add(NodeCatalog.OutputTypeId, 1920, 560, (NodeCatalog.OutputGainPort, 0.6f));

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

        // One clock and two speeds off it, rather than two clocks. An output
        // fans out to as many inputs as you like, so what a patch needs more
        // than one of is the scaling, not the time.
        //
        // It is here at all only because both speeds are scaled. The Rotate's
        // own x and y need no such module: they are normalled to Coordinates
        // and are already reading the pixel's position (ADR-0050).
        var clock = b.Add("time", 40, 260);
        var spin = b.Add("math.mul", 150, 60, (1, 0.15f));
        var drift = b.Add("math.mul", 150, 460, (1, 0.3f));

        var rotate = b.Add("space.rotate", 260, 160);
        var fold = b.Add("space.kaleidoscope", 470, 200, (2, 6f));
        var noise = b.Add("pattern.noise", 680, 240, (3, 2.5f));
        var color = b.Add("color.hsv", 890, 240, (1, 0.9f), (2, 1f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1090, 260);

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

        // Here for the Rings' 'offset' and nothing else — the two oscillators
        // and the Rings' own x and y are normalled and take no wire (ADR-0050).
        // What is left is the one socket in the patch that has to be told to
        // move, which is what a Time module is now for.
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

        b.Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, tremolo, 0)
         .Wire(slow, 0, tremolo, 1)
         .Wire(tremolo, 0, output, NodeCatalog.OutputLeftPort)
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

        // 'clock' is the sweep's own time base and takes no wire — it is a
        // domain, so it is normalled to Time; 'rate' is the pitch; 'radius' and
        // 'x' choose which loop through the field is read.
        var scan = b.Add(NodeCatalog.ScanTypeId, 800, 420, (3, 0.35f), (6, 1f));

        // Eye: the field under the trace, dim enough that the loop reads on top
        // of it rather than competing with it.
        var glow = b.Add("math.remap", 540, 120, (1, -1f), (2, 1f), (3, 0.05f), (4, 0.55f));
        var tint = b.Add("color.hsv", 800, 120, (1, 0.7f));
        var lit = b.Add("math.add", 1080, 220);

        var output = b.Add(NodeCatalog.OutputTypeId, 1320, 300, (NodeCatalog.OutputGainPort, 0.45f));

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

        // Here for 'radius', which is the one Coordinates output nothing is
        // normalled to: x and y arrive at a module that wants a position on
        // their own, and a distance from the centre never does.
        var coord = b.Add("coord", 40, 320);

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

        b.Wire(coord, 2, spread, 0)
         .Wire(ramp, 0, sweep, 0)
         .Wire(spread, 0, sweep, 1)
         .Wire(sweep, 0, note, 1)
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
    /// A noise field played as a melody and drawn as the terraces it is being
    /// snapped to: one Quantiser, in a pentatonic, feeding both sinks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Noise is one module read two ways, which is what makes the picture
    /// honest rather than illustrative. On the audio path x and y are nothing —
    /// there is no pixel — so what the ear gets is a walk along <c>z</c> alone: a
    /// value stepping once a beat, and a melody once the scale has pulled it
    /// onto notes. On the video path the same module reads the pixel's own
    /// position at that same <c>z</c>, so what the eye gets is the walk laid out
    /// across the screen. The note you hear is the color at the middle of the
    /// picture.
    /// </para>
    /// <para>
    /// The two sinks part company at the Sample & Hold, which is the other
    /// module here that is read two ways. The ear needs the field to stop moving
    /// between one note and the next; the eye needs it not to, or the picture
    /// would snap on the beat instead of drifting. A Hold is exactly that
    /// difference — it holds where there is a before to hold from and is a wire
    /// where there is not, so the speakers get a melody in steps and the screen
    /// goes on drifting, out of one module and one wire.
    /// </para>
    /// <para>
    /// A pentatonic because it is the scale with no wrong note in it: a signal
    /// with no idea what key it is in lands somewhere musical whatever it does,
    /// which is the whole argument for quantising in the first place. The five
    /// notes are also what makes the terraces uneven — the gaps between them are
    /// two and three semitones rather than one, so the bands are visibly
    /// different widths and the widths <em>are</em> the scale.
    /// </para>
    /// <para>
    /// Hue is quantised and brightness is not, which is the before and after of
    /// the snap in one picture: hard-edged bands of color sitting inside the
    /// smooth field they were cut from. Chromatic does the same trick a semitone
    /// at a time; this one does it a scale at a time, and the difference between
    /// the two pictures is the difference between the two modules.
    /// </para>
    /// <para>
    /// The pitch steps and the tone does not click, for ADR-0030's reason — the
    /// oscillator carries its phase, so a frequency that jumps bends the waveform
    /// rather than breaking it. Nothing here smooths anything, and the one place
    /// that turned out to be a liability is why there is a Hold in it at all:
    /// not clicking is exactly what makes a pitch change mid-note sound like a
    /// slide instead of like a mistake.
    /// </para>
    /// </remarks>
    public static Patch InKey(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // One tempo for the whole patch, because the two things it sets have to
        // be the same number: how often a note is plucked, and how often the
        // melody is allowed to move. 180 a minute is three a second.
        var tempo = b.Add(NodeCatalog.TempoTypeId, 40, 700, (0, 180f));

        // For the field's 'z', which is the one socket in the patch that has to
        // be told to move: 'z' is depth through the noise rather than a position
        // in it, so nothing is normalled to it (ADR-0050).
        var time = b.Add("time", 40, 460);

        var wander = b.Add("math.mul", 250, 460, (1, 0.3f));

        // The one module both sinks read, and the reason they hear and see the
        // same thing. Its x and y need no wire: on the screen they are the
        // pixel's own, and at the speakers there is no pixel and they are zero.
        var field = b.Add("pattern.noise", 880, 300, (3, 2.2f));

        // Two octaves from A2, which is low enough to sound like a bass line at
        // the bottom and high enough to sing at the top.
        var range = b.Add("math.remap", 1090, 300, (1, 0f), (2, 1f), (3, 45f), (4, 69f));

        // A minor pentatonic — A C D E G, the same five notes as C major
        // pentatonic. The scale is a set on the module rather than sockets on it
        // (ADR-0051), so nothing is wired here and the notes are on the node.
        //
        // Its 'hold' is the whole difference between a melody and a glide, and
        // it took hearing it to find. A note's pitch has to be settled before the
        // note starts and stay settled until it has finished — and a field that
        // moves freely crosses into the next note of the scale at whatever moment
        // it happens to, which is as often as not in the middle of one. The pitch
        // steps cleanly when it does (ADR-0030 is what stops that clicking), and
        // a clean step in the middle of a sounding note is not heard as a new
        // note at all: with no onset to mark it, the ear takes it for the note it
        // was already listening to, sliding.
        //
        // The gate that opens the envelope goes into 'hold' as well, so the
        // interval the note is frozen for is the interval it is sounding for, by
        // construction rather than by arithmetic.
        var key = b.Add(NodeCatalog.QuantiserTypeId, 1300, 300);
        ScaleExtra.Set(key, [0, 2, 4, 7, 9]);

        // Ear: the snapped note as a pitch, plucked three times a second.
        var note = b.Add("audio.note", 1520, 480);
        var tone = b.Add("osc.sine", 1740, 480);

        // 'width' is how much of each beat the trigger is shut for, so a small
        // one opens just after the beat and holds until just before the next.
        var beat = b.Add("osc.pulse", 1300, 700, (3, 0.12f));

        // Percussive: nothing sustained, so a note has decayed to silence well
        // inside its own beat, and the pitch the Hold catches on the next one
        // lands on a note starting rather than on one still ringing.
        var pluck = b.Add(NodeCatalog.AdsrTypeId, 1520, 700, (1, -2.4f), (2, -0.85f), (3, 0f), (4, -1.5f));
        var struck = b.Add("math.mul", 1960, 560);

        // Eye: the snapped note as a hue, one turn of the wheel to the octave —
        // so a note is the same color wherever on screen it turns up, and the
        // one at the middle is the one being played.
        //
        // Wrapped rather than run across the whole range, which was the first
        // thing tried and does not work: two and a half octaves spread over one
        // sweep of hue puts adjacent notes a twentieth of the wheel apart, and
        // the terraces come out as a gradient with faint creases in it. Per
        // octave, the five notes of the scale are a sixth of the wheel apart at
        // the closest, which is the difference between a band and a crease.
        var wheel = b.Add("math.mul", 1520, 40, (1, 1f / 12f));
        var octave = b.Add("math.fract", 1520, 120);

        // Warm at the bottom of the octave and cool at the top, over rather less
        // than the whole wheel: a full turn puts red beside green beside purple,
        // which reads as a test card rather than as a field with steps in it.
        // Half a turn keeps neighbouring notes related and still tells them
        // apart, and the seam where an octave rolls over is a real edge.
        var height = b.Add("math.remap", 1520, 200, (1, 0f), (2, 1f), (3, 0.02f), (4, 0.6f));

        // And the field itself as brightness, unsnapped. This is the whole
        // demonstration: the gradient is what arrived and the bands are what the
        // Quantiser made of it, and both are on screen at once.
        var glow = b.Add("math.remap", 1520, 320, (1, 0f), (2, 1f), (3, 0.22f), (4, 0.95f));
        var map = b.Add("color.hsv", 1740, 160, (1, 0.6f));

        var output = b.Add(NodeCatalog.OutputTypeId, 2180, 320, (NodeCatalog.OutputGainPort, 0.55f));

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

        // One clock read at three speeds, so nothing in the picture ever quite
        // lines up with anything else and it does not visibly loop. The three
        // are Multiplies rather than three Times: seconds are seconds, and what
        // differs between these is only how much of them each part wants.
        //
        // Nineteen modules and this is the only source in the patch. Both
        // geometry chains start from a Rotate or a Scale whose x and y are
        // normalled to Coordinates, so neither needs anything in front of it.
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

        return b.Patch;
    }

    /// <summary>
    /// The camera-pointed-at-its-own-monitor patch: each frame is re-read
    /// slightly rotated, scaled and dimmed, with fresh rings fed in on top.
    /// </summary>
    public static Patch FeedbackTunnel(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

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

        // For 'radius', which is what makes the eye's half of each voice a
        // standing field rather than a travelling tone. Nothing else in the
        // patch needs a source: every oscillator's 'in' is normalled to Time,
        // and the four that are heard take it as it comes.
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

            b.Wire(pitch, 0, tone, 1)
             .Wire(tone, 0, chord, channel)
             .Wire(level, 0, chord, channel + 1)

             .Wire(coord, 2, band, 0)
             .Wire(band, 0, tint, 2)
             .Wire(tint, 0, picture, channel)
             .Wire(level, 0, picture, channel + 1);
        }

        return b.Patch;
    }

    /// <summary>
    /// Four instruments off four sequencers, and one picture off three of them.
    /// A bass line, a two-oscillator lead with a fifth over it, a kick and a
    /// hi-hat, mixed to a stereo pair — and the same steps that play them are
    /// what turns, folds, colors and lights the image.
    /// </summary>
    /// <remarks>
    /// The largest patch in the box, and it is here to show what one patch can
    /// be rather than to teach a single idea the way the others do. Every module
    /// in it is one the palette already holds, and nothing it does with them is
    /// something the smaller presets have not each done once.
    /// <para>
    /// One Tempo drives all four sequencers and nothing anywhere is timed off
    /// anything else. The bass runs in eighths and the lead, the kick and the
    /// hats in sixteenths — two Multiplies off the one knob, which is the whole
    /// of the tempo structure. The bass is twelve notes of uneven length adding
    /// to two bars and the lead is twenty even ones adding to five beats, so the
    /// two come back into line every ten bars rather than every one, and the
    /// tune stops sounding like a loop long before the picture stops looking
    /// like one.
    /// </para>
    /// <para>
    /// Which sequencer drives which part of the image is the decision worth
    /// reading. The bass moves the geometry: it changes chord about once a bar,
    /// so the rotation and the number of kaleidoscope wedges change with it, at
    /// a rate the eye can follow. The lead moves the color: it steps four times
    /// a beat, which is far too fast to rebuild a shape with and exactly right
    /// for hue. The kick moves the light — the zoom pump, the brightness and the
    /// twist on the feedback are all its gate. The hats are heard and not seen,
    /// which is the one instrument that is: a sixteenth-note shimmer would read
    /// as a flicker rather than as a rhythm.
    /// </para>
    /// <para>
    /// The kick's gate is read on the video path rather than an envelope of it,
    /// because an envelope has no memory drawn and hands over its gate anyway.
    /// That works here where it did not in <see cref="Kick"/> only because this
    /// gate comes from a sequencer rather than from a Pulse: it is open for
    /// about a third of a sixteenth, which at thirty frames a second is a frame
    /// or two of flash against four of dark rather than the other way about.
    /// </para>
    /// <para>
    /// The hi-hat has no noise module behind it because there is no noise module
    /// that would do: Noise is a field in x and y, and the audio path stands at
    /// one point of the plane, so a hat made of it would be a held tone. What
    /// makes the hiss instead is the hash every shader writes — the fraction of
    /// a sine of a large multiple of the clock, which lands somewhere else
    /// entirely from one sample to the next. It is a constant per frame on the
    /// video path, where it is never read, and it is the only place in the
    /// preset where a number is chosen for being large rather than for meaning
    /// something.
    /// </para>
    /// <para>
    /// Stereo comes from the detune and not from a pan knob. The lead is two
    /// saws a few cents apart, and the two Mixers differ in which of them they
    /// take — left gets the one at pitch, right gets the one the vibrato is
    /// bending — so the width is the beating between them rather than one signal
    /// made quieter on one side. The hats lean right by the same trick, and the
    /// kick and the bass sit dead centre, which is where a kick and a bass
    /// belong.
    /// </para>
    /// <para>
    /// Both sinks are clipped rather than trusted. Four instruments through a
    /// Mixer sum the way a desk sums, so the master Multiply is deliberately
    /// past unity and the Clamp after it is what makes that safe; the picture is
    /// clamped before the HSV for the same reason, because a value past one is
    /// not brighter, it is only wrong.
    /// </para>
    /// </remarks>
    public static Patch WholeBand(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // --- the clock -------------------------------------------------------

        // Here for the things that have to be told to move and are not an 'in':
        // the Noise's z, three drift rates, and the hash the hats are made of.
        var clock = b.Add("time", 40, 1560);

        var tempo = b.Add(NodeCatalog.TempoTypeId, 40, 1840, (0, 112f));
        var eighths = b.Add("math.mul", 250, 1840, (1, 2f));
        var sixteenths = b.Add("math.mul", 250, 2000, (1, 4f));

        // --- the sequences ---------------------------------------------------

        // Am, G, F, E over two bars of eighths, with the last chord held. The
        // lengths are uneven and that is the groove: the dotted eighth into a
        // sixteenth at the top of each bar is what stops it walking.
        var bassSeq = b.Add("seq.notes", 480, 1620, (2, 0.55f), (3, 0.02f));
        StepsExtra.Set(bassSeq,
        [
            new Step(33f, 1.5f), new Step(33f, 0.5f, 0.55f),
            new Step(40f, 1f, 0.8f), new Step(33f, 1f, 0.65f),
            new Step(31f, 1.5f), new Step(31f, 0.5f, 0.55f),
            new Step(38f, 1f, 0.8f), new Step(31f, 1f, 0.65f),
            new Step(29f, 1.5f), new Step(36f, 0.5f, 0.6f),
            new Step(29f, 2f, 0.85f), new Step(28f, 4f),
        ]);

        // Twenty sixteenths — five beats, against the bass's eight. The rests
        // are a volume rather than a note, which is what a volume being a level
        // and not a switch is for: the pitch stays where it was, so the notes
        // either side of a rest are one phrase rather than three.
        var leadSeq = b.Add("seq.notes", 480, 2000, (2, 0.62f), (3, 0.045f));
        StepsExtra.Set(leadSeq,
        [
            new Step(69f), new Step(72f, 1f, 0.8f), new Step(76f, 1f, 0.9f), new Step(72f, 1f, 0.6f),
            new Step(77f), new Step(76f, 1f, 0.85f), new Step(76f, 1f, 0f), new Step(74f, 1f, 0.9f),
            new Step(71f, 1f, 0.8f), new Step(74f, 1f, 0.7f), new Step(79f), new Step(77f, 1f, 0.85f),
            new Step(76f, 1f, 0.9f), new Step(74f, 1f, 0.6f), new Step(72f, 1f, 0.95f), new Step(72f, 1f, 0f),
            new Step(71f, 1f, 0.85f), new Step(69f), new Step(67f, 1f, 0.7f), new Step(69f, 1f, 0.8f),
        ]);

        // The drum pattern, as volumes: the four beats, a ghost off the second
        // and another at the end of the bar, which is what makes it a groove
        // rather than a metronome. A step's own value is nothing to do with the sound
        // here — the Note Sequencer's would be a pitch and this one's is spare,
        // so it rests at zero and the volumes carry the whole pattern.
        var kickSeq = b.Add("seq.values", 480, 2380, (2, 0.32f), (3, 0.01f));
        StepsExtra.Set(kickSeq,
        [
            new Step(0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.9f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.5f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.95f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.85f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.55f), new Step(0f, 1f, 0f),
        ]);

        // Here the value is used, and it is the one gesture that needs a
        // Sequencer rather than a Note Sequencer: it opens the hat. The step
        // goes to the decay knob of the hat's envelope, so a high step rings for
        // a seventh of a second and a low one is a tick — closed hats all the
        // way through with two open ones in the bar, out of a list of numbers
        // rather than out of two instruments.
        var hatSeq = b.Add("seq.values", 480, 2760, (2, 0.4f), (3, 0.01f));
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

        // --- bass ------------------------------------------------------------

        // A saw at the note and a sine an octave under it. The sub is a second
        // Note rather than an oscillator at half the frequency, because a pitch
        // here is a note number and an octave is a socket on the module that
        // knows what one of those is.
        var bassNote = b.Add("audio.note", 730, 1560);
        var subNote = b.Add("audio.note", 730, 1780, (1, -1f));

        var bassSaw = b.Add("osc.saw", 960, 1500, (3, 0.8f));
        var bassSub = b.Add("osc.sine", 960, 1720, (3, 0.6f));
        var bassSum = b.Add("math.add", 1190, 1560);

        var bassEnv = b.Add(NodeCatalog.AdsrTypeId, 960, 1920,
            (1, -3f), (2, -1.1f), (3, 0.35f), (4, -1.2f));

        var bassVca = b.Add("math.mul", 1420, 1560);

        // Overdriven and then clipped, which is the cheapest waveshaper there
        // is: everything under the wall passes and everything over it flattens,
        // and a flattened saw is a saw with more harmonics in it. Only the bass
        // is treated this way — the same two modules across the lead would take
        // its envelope off it and leave a drone.
        var bassHot = b.Add("math.mul", 1650, 1560, (1, 2.4f));
        var bassOut = b.Add("math.clamp", 1880, 1560, (1, -1f), (2, 1f));

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

        // --- lead ------------------------------------------------------------

        var leadNote = b.Add("audio.note", 730, 2000);

        // The twin, taken off the first Note's 'note' output rather than off the
        // sequencer again: it is the same snapped number, and the detune is put
        // on after the snap because cents are the one control that can sit
        // between two semitones. Nine of them, swung by a slow sine, so the pair
        // beat against each other at a rate that keeps changing.
        var vibrato = b.Add("osc.sine", 730, 2200, (1, 5.4f), (3, 9f));
        var wide = b.Add("audio.note", 960, 2180);

        var leadA = b.Add("osc.saw", 1190, 1980, (3, 0.7f));
        var leadB = b.Add("osc.saw", 1190, 2160, (3, 0.7f));

        // A fifth over the tune, on a triangle so it fills rather than competes,
        // and faded in and out by a sine slow enough that it is never quite the
        // same phrase twice. Adding seven before the Note is the interval: the
        // sequencer hands out note numbers, and seven of those is a fifth.
        var fifth = b.Add("math.add", 730, 2560, (1, 7f));
        var fifthNote = b.Add("audio.note", 960, 2560);
        var fifthOsc = b.Add("osc.triangle", 1190, 2560, (3, 0.5f));
        var swell = b.Add("osc.sine", 960, 2780, (1, 0.043f), (3, 0.5f), (4, 0.5f));
        var fifthLevel = b.Add("math.mul", 1420, 2560);

        // Left and right differ in which saw they carry and in nothing else,
        // which is where the width comes from.
        var stackL = b.Add("math.add", 1420, 1980);
        var stackR = b.Add("math.add", 1420, 2180);

        var leadEnv = b.Add(NodeCatalog.AdsrTypeId, 1190, 2340,
            (1, -2.7f), (2, -1.15f), (3, 0.28f), (4, -1.4f));

        var voiceL = b.Add("math.mul", 1650, 1980);
        var voiceR = b.Add("math.mul", 1650, 2180);

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

        // --- kick ------------------------------------------------------------

        // Two envelopes and a sine, which is the whole of a kick drum: one
        // shapes how loud it is and the shorter one shapes what pitch it is. See
        // <see cref="Kick"/> for why the second of those is the difference
        // between a drum and a beep.
        var kickLevel = b.Add(NodeCatalog.AdsrTypeId, 730, 2380,
            (1, -2.9f), (2, -0.62f), (3, 0f), (4, -1.1f));

        var kickSweep = b.Add(NodeCatalog.AdsrTypeId, 730, 2960,
            (1, -3.3f), (2, -1.4f), (3, 0f), (4, -1.8f));

        var kickPitch = b.Add("math.remap", 960, 2960, (1, 0f), (2, 1f), (3, 47f), (4, 205f));
        var kickBody = b.Add("osc.sine", 1190, 2960);
        var kickOut = b.Add("math.mul", 1420, 2900);

        b.Wire(kickSeq, 1, kickLevel, 0)
         .Wire(kickSeq, 1, kickSweep, 0)
         .Wire(kickSweep, 0, kickPitch, 0)
         .Wire(kickPitch, 0, kickBody, 1)
         .Wire(kickBody, 0, kickOut, 0)
         .Wire(kickLevel, 0, kickOut, 1);

        // --- hats ------------------------------------------------------------

        // The hiss. Nothing in the catalogue makes a noise a point in the plane
        // can hear — see the remarks — so it is built: a large multiple of the
        // clock, a sine of it, a larger multiple of that, and the fraction.
        var grain = b.Add("math.mul", 730, 3180, (1, 3571f));
        var hash = b.Add("math.sin", 960, 3180);
        var scatter = b.Add("math.mul", 1190, 3180, (1, 4371.3f));
        var white = b.Add("math.fract", 1420, 3180);
        var hiss = b.Add("math.remap", 1650, 3180, (1, 0f), (2, 1f), (3, -1f), (4, 1f));

        // The step, as a decay time. That knob is in decades, so this is three
        // milliseconds at the bottom of the sequence and a seventh of a second
        // at the top of it.
        var hatOpen = b.Add("math.remap", 730, 2760, (1, 0f), (2, 1f), (3, -2.5f), (4, -0.85f));
        var hatEnv = b.Add(NodeCatalog.AdsrTypeId, 960, 2760, (1, -3.7f), (3, 0f), (4, -2.2f));
        var hatOut = b.Add("math.mul", 1880, 3060);

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

        // --- the desk --------------------------------------------------------

        var deskL = b.Add("math.mixer", 2110, 1900, (1, 0.55f), (3, 0.72f), (5, 1f), (7, 0.55f));
        var deskR = b.Add("math.mixer", 2110, 2400, (1, 0.55f), (3, 0.72f), (5, 1f), (7, 0.8f));

        var driveL = b.Add("math.mul", 2340, 1900, (1, 1.2f));
        var driveR = b.Add("math.mul", 2340, 2400, (1, 1.2f));

        var limitL = b.Add("math.clamp", 2570, 1900, (1, -1f), (2, 1f));
        var limitR = b.Add("math.clamp", 2570, 2400, (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, 3240, 1180, (NodeCatalog.OutputGainPort, 0.62f));

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

        // --- the picture: geometry -------------------------------------------

        // One clock read at four speeds. They are Multiplies rather than four
        // Times for the reason Nebula gives: seconds are seconds, and what
        // differs between these is only how much of them each part wants.
        var spin = b.Add("math.mul", 250, 120, (1, 0.055f));
        var boil = b.Add("math.mul", 250, 460, (1, 0.18f));
        var drift = b.Add("math.mul", 250, 620, (1, 0.4f));
        var crawl = b.Add("math.mul", 250, 780, (1, 0.02f));

        // The bass moves the frame: where it has got to in the pattern is added
        // to the rotation, and is how many wedges the fold has. It changes chord
        // about once a bar, which is slow enough that the picture rebuilding
        // itself reads as an arrangement rather than as a fault.
        var stride = b.Add("math.remap", 250, 280, (1, 0f), (2, 1f), (3, -0.4f), (4, 0.4f));
        var angle = b.Add("math.add", 480, 180);
        var turn = b.Add("space.rotate", 710, 140);

        // The kick moves the light. Its gate is read directly rather than
        // through an envelope — see the remarks — and it is doing three things
        // at once: the zoom, the brightness, and the twist on the feedback.
        var pump = b.Add("math.remap", 480, 380, (1, 0f), (2, 1f), (3, 0.96f), (4, 1.3f));
        var zoom = b.Add("space.scale", 940, 160);

        var segments = b.Add("math.remap", 710, 400, (1, 0f), (2, 1f), (3, 3f), (4, 10f));
        var fold = b.Add("space.kaleidoscope", 1170, 180);

        // Geometry alone looks like geometry, so the plane is bent by a field
        // read from inside the fold — symmetric, so it repeats with the wedges
        // instead of quietly undoing them. Nebula's trick, and in Nebula's
        // order: fold first, warp inside it.
        var field = b.Add("pattern.noise", 1400, 400, (3, 2.1f));
        var breath = b.Add("osc.sine", 1170, 620, (1, 0.071f), (3, 0.5f), (4, 0.5f));
        var reach = b.Add("math.remap", 1400, 620, (1, 0f), (2, 1f), (3, 0.2f), (4, 0.7f));
        var bend = b.Add("space.warp", 1630, 200);

        // The lead's gate widens the rings, so a sixteenth arrives as a band
        // rather than as a change of color alone.
        var count = b.Add("math.remap", 1400, 780, (1, 0f), (2, 1f), (3, 2.6f), (4, 5.5f));
        var bands = b.Add("pattern.rings", 1860, 220);

        // Rings are a sine, so most of the frame is dark and only the crests
        // survive as filaments.
        var filament = b.Add("math.smoothstep", 2090, 260, (0, 0.2f), (1, 0.95f));

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

        // --- the picture: color ----------------------------------------------

        // The lead moves the hue. The field and the slowest of the four clocks
        // are added under it so that the same step of the tune is never quite
        // the same color twice, and the whole is wrapped rather than clamped,
        // because a hue is a wheel.
        var stepped = b.Add("math.mul", 1400, 940, (1, 0.8f));
        var wash = b.Add("math.mul", 1630, 940, (1, 0.9f));
        var blend = b.Add("math.add", 1860, 940);
        var slide = b.Add("math.add", 2090, 940);
        var hue = b.Add("math.fract", 2320, 940);

        // The bass's gate takes the color out of the image between its notes,
        // which is the same rhythm the ear is getting from it.
        var saturation = b.Add("math.remap", 2320, 780, (1, 0f), (2, 1f), (3, 0.55f), (4, 0.95f));

        var glow = b.Add("math.remap", 1860, 560, (1, 0f), (2, 1f), (3, 0.75f), (4, 1.7f));
        var lit = b.Add("math.mul", 2320, 340);
        var visible = b.Add("math.clamp", 2550, 340, (1, 0f), (2, 1f));

        var fresh = b.Add("color.hsv", 2780, 620);

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

        // --- the picture: feedback -------------------------------------------

        // Two readings of the last frame rather than one, turning opposite ways:
        // one zoomed in a little and one out, with the red taken from the first
        // and the green and blue from the second. What that makes is a chromatic
        // tunnel — the fringes drift apart as the trail ages, the way a lens
        // splits light, and there is no lens anywhere in it.
        var inward = b.Add("space.scale", 1170, 1080, (2, 1.035f));
        var twist = b.Add("math.remap", 1170, 1240, (1, 0f), (2, 1f), (3, 0.012f), (4, 0.05f));
        var inTurn = b.Add("space.rotate", 1400, 1080);
        var pastIn = b.Add("feedback", 1630, 1080);
        var warm = b.Add("color.split", 1860, 1080);

        var outward = b.Add("space.scale", 1170, 1380, (2, 0.972f));
        var outTurn = b.Add("space.rotate", 1400, 1380, (2, -0.016f));
        var pastOut = b.Add("feedback", 1630, 1380);
        var cool = b.Add("color.split", 1860, 1380);

        var ghost = b.Add("color.rgb", 2320, 1180);
        var trail = b.Add("color.gain", 2550, 1180, (1, 0.85f), (2, 0f));

        // Max rather than a blend, for FeedbackTunnel's reason: a trail brighter
        // than the new frame keeps its brightness, which is what makes a streak
        // read as a streak rather than as a smeared copy.
        var combine = b.Add("math.max", 3010, 900);

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

        return b.Patch;
    }
}
