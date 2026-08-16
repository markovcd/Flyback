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
        new("Chromatic", Chromatic),
        new("Empty", Empty),
    ];

    public static Patch Default() => Plasma(NodeCatalog.Current);

    /// <summary>Just an Output node with something to plug into.</summary>
    public static Patch Empty(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);
        b.Add(NodeCatalog.VideoOutputTypeId, 640, 260);
        return b.Patch;
    }

    /// <summary>Two sine fields crossed and read as hue — the "hello world" of video synths.</summary>
    public static Patch Plasma(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 200);
        var time = b.Add("time", 40, 400, (0, 0.2f));

        // Sine along x, and a second along y whose phase drifts with time.
        var horizontal = b.Add("osc.sine", 260, 120, (1, 1.5f));
        var vertical = b.Add("osc.sine", 260, 300, (1, 1.1f));

        var sum = b.Add("math.add", 500, 200);
        var hue = b.Add("math.remap", 660, 200, (1, -2f), (2, 2f), (3, 0f), (4, 1f));
        var colour = b.Add("colour.hsv", 860, 200, (1, 0.85f), (2, 1f));
        var output = b.Add(NodeCatalog.VideoOutputTypeId, 1060, 220);

        b.Wire(coord, 0, horizontal, 0)
         .Wire(coord, 1, vertical, 0)
         .Wire(time, 0, vertical, 2)
         .Wire(horizontal, 0, sum, 0)
         .Wire(vertical, 0, sum, 1)
         .Wire(sum, 0, hue, 0)
         .Wire(hue, 0, colour, 0)
         .Wire(colour, 0, output, 0);

        return b.Patch;
    }

    /// <summary>Rotating wedges filled with noise that boils over time.</summary>
    public static Patch Kaleidoscope(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 220);
        var spin = b.Add("time", 40, 60, (0, 0.15f));
        var drift = b.Add("time", 40, 460, (0, 0.3f));

        var rotate = b.Add("space.rotate", 260, 160);
        var fold = b.Add("space.kaleidoscope", 470, 200, (2, 6f));
        var noise = b.Add("pattern.noise", 680, 240, (3, 2.5f));
        var colour = b.Add("colour.hsv", 890, 240, (1, 0.9f), (2, 1f));
        var output = b.Add(NodeCatalog.VideoOutputTypeId, 1090, 260);

        b.Wire(coord, 0, rotate, 0)
         .Wire(coord, 1, rotate, 1)
         .Wire(spin, 0, rotate, 2)
         .Wire(rotate, 0, fold, 0)
         .Wire(rotate, 1, fold, 1)
         .Wire(fold, 0, noise, 0)
         .Wire(fold, 1, noise, 1)
         .Wire(drift, 0, noise, 2)
         .Wire(noise, 0, colour, 0)
         .Wire(colour, 0, output, 0);

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
        var time = b.Add("time", 40, 380, (0, 1f));

        // The shared control signal, remapped to 0..1 by amp and bias.
        var slow = b.Add("osc.sine", 260, 340, (1, 0.15f), (3, 0.5f), (4, 0.5f));

        // Ear.
        var pitch = b.Add("audio.frequency", 260, 560, (0, 110f));
        var tone = b.Add("osc.sine", 470, 560, (1, 110f));
        var tremolo = b.Add("math.mul", 700, 580);
        var speaker = b.Add(NodeCatalog.AudioOutputTypeId, 900, 600, (2, 0.6f));

        // Eye.
        var rings = b.Add("pattern.rings", 260, 80, (2, 3f));
        var tint = b.Add("colour.hsv", 700, 140, (1, 0.85f));
        var screen = b.Add(NodeCatalog.VideoOutputTypeId, 920, 160);

        b.Wire(time, 0, slow, 0)
         .Wire(time, 0, tone, 0)
         .Wire(pitch, 0, tone, 1)
         .Wire(tone, 0, tremolo, 0)
         .Wire(slow, 0, tremolo, 1)
         .Wire(tremolo, 0, speaker, 0)
         .Wire(coord, 0, rings, 0)
         .Wire(coord, 1, rings, 1)
         .Wire(time, 0, rings, 3)
         .Wire(slow, 0, tint, 0)
         .Wire(rings, 0, tint, 2)
         .Wire(tint, 0, screen, 0);

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
    /// flat rings of colour rather than a gradient. Feed the ramp straight to a
    /// Frequency knob instead and both become continuous.
    /// <para>
    /// Everything the ear hears is continuous even so, which is not the same
    /// thing as smooth: the notes are flat and the changes between them are not
    /// instant. They cannot be. An oscillator's phase here is its input times
    /// its freq, so a freq that jumps jumps the phase with it — by more the
    /// longer the patch has been running — and a jump in the waveform is a
    /// click. The Note module's glide is what keeps the steps hard and the
    /// waveform whole; turn it to 0 and this preset clicks three times a second.
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
        var time = b.Add("time", 40, 100, (0, 1f));

        // Half an octave either way: six semitones above the note on the knob
        // down to six below over four seconds, and back up over the next four.
        // A triangle rather than a saw because a saw's reset is a jump, and a
        // jump in the ramp is one place the Note module's glide cannot help —
        // there is no room between two notes to slide across twelve of them.
        var ramp = b.Add("osc.triangle", 250, 100, (1, 0.125f), (3, 0.5f));

        // Distance from the centre on the same scale, subtracted rather than
        // added so the rings travel outward as the ramp climbs.
        var spread = b.Add("math.mul", 250, 340, (1, 0.6f));
        var sweep = b.Add("math.sub", 460, 200);

        // A3 on the knob, and whatever arrives snapped to the nearest semitone.
        var note = b.Add("audio.note", 660, 220);

        // Ear.
        var tone = b.Add("osc.sine", 880, 460);
        var speaker = b.Add(NodeCatalog.AudioOutputTypeId, 1100, 480, (2, 0.5f));

        // Eye: one hue per semitone, wrapped so an octave is a full turn of the
        // wheel and the same note always comes out the same colour.
        var wheel = b.Add("math.mul", 880, 100, (1, 1f / 12f));
        var hue = b.Add("math.fract", 1080, 100);

        // Brightness is taken from the ramp itself rather than from the note, so
        // the two are in one picture: a smooth glow with hard-edged colour rings
        // sitting in it, which is the before and after of the snap.
        var glow = b.Add("math.remap", 1080, 300, (1, -1.8f), (2, 0.5f), (3, 0.08f), (4, 1f));

        var colour = b.Add("colour.hsv", 1270, 140, (1, 0.85f));
        var screen = b.Add(NodeCatalog.VideoOutputTypeId, 1470, 160);

        b.Wire(time, 0, ramp, 0)
         .Wire(coord, 2, spread, 0)
         .Wire(ramp, 0, sweep, 0)
         .Wire(spread, 0, sweep, 1)
         .Wire(sweep, 0, note, 1)
         .Wire(time, 0, tone, 0)
         .Wire(note, 0, tone, 1)
         .Wire(tone, 0, speaker, 0)
         .Wire(note, 1, wheel, 0)
         .Wire(wheel, 0, hue, 0)
         .Wire(sweep, 0, glow, 0)
         .Wire(hue, 0, colour, 0)
         .Wire(glow, 0, colour, 2)
         .Wire(colour, 0, screen, 0);

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
    /// Feeding that same field into the hue is what ties shape to colour, so it
    /// reads as one moving thing rather than a pattern with a palette applied to
    /// it. And it is the most expensive preset here by some way, which is also
    /// the point of having it: it is what the renderer looks like under load.
    /// </para>
    /// </remarks>
    public static Patch Nebula(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var coord = b.Add("coord", 40, 300);

        // Three clocks at different rates, so nothing in the picture ever quite
        // lines up with anything else and it does not visibly loop.
        var spin = b.Add("time", 40, 80, (0, 0.05f));
        var boil = b.Add("time", 40, 560, (0, 0.12f));
        var pulse = b.Add("time", 40, 720, (0, 0.2f));

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

        var fresh = b.Add("colour.hsv", 1270, 400, (1, 0.85f));

        // The previous frame, zoomed out a hair and turned, so what is already on
        // screen spirals outward while new filaments arrive underneath it.
        var widen = b.Add("space.scale", 250, 900, (2, 0.99f));
        var swirl = b.Add("space.rotate", 450, 900, (2, 0.015f));
        var previous = b.Add("feedback", 660, 900);
        var trail = b.Add("colour.gain", 870, 900, (1, 0.92f), (2, 0f));

        // Max rather than a blend: a trail that is brighter than the new frame
        // keeps its brightness, which is what makes the streaks read as trails
        // rather than as a smeared copy.
        var combine = b.Add("math.max", 1470, 620);
        var screen = b.Add(NodeCatalog.VideoOutputTypeId, 1660, 640);

        b.Wire(coord, 0, turn, 0)
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
         .Wire(combine, 0, screen, 0);

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
        var spin = b.Add("time", 40, 60, (0, 0.08f));
        var pulse = b.Add("time", 40, 560, (0, 0.25f));

        var rotate = b.Add("space.rotate", 250, 140);
        var scale = b.Add("space.scale", 440, 160, (2, 1.05f));
        var previous = b.Add("feedback", 620, 180);
        var dim = b.Add("colour.gain", 790, 180, (1, 0.95f), (2, 0f));

        // Fresh material: bright rings that travel outward.
        var rings = b.Add("pattern.rings", 250, 460, (2, 1.5f));
        var spark = b.Add("math.smoothstep", 450, 500, (0, 0.8f), (1, 1f));
        var tint = b.Add("colour.hsv", 640, 520, (1, 1f));

        var combine = b.Add("math.max", 950, 300);
        var output = b.Add(NodeCatalog.VideoOutputTypeId, 1130, 320);

        b.Wire(coord, 0, rotate, 0)
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
}
