using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A generative patch with no clock in it, played into the two effects this
/// plugin is for. Three random voltages read out of a noise field choose the
/// notes, open the voices and move the picture; nothing anywhere is counting
/// beats, so there is no bar for any of it to come round on.
/// </summary>
internal static class SlowWeatherPreset
{
    public const string Name = "Slow weather";

    /// <summary>
    /// The one module here that is not this plugin's. The three sweeps live
    /// beside this preset now; the Filter is in Voice, so this is still a patch
    /// that reaches across a boundary and still has to say so when the other
    /// plugin is not there.
    /// </summary>
    private const string Voice = "flyback.voice";

    private const string Chorus = "flyback.effects.chorus";
    private const string Phaser = "flyback.effects.phaser";
    private const string Flanger = "flyback.effects.flanger";
    private const string Filter = "flyback.voice.filter";

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        var b = new PatchBuilder(modules);

        // --- three random voltages -------------------------------------------

        var clock = b.Add("time");

        // Where in the field each voltage is read. A knob and two wires, and it
        // is the whole of what turns Noise from a texture into a source — see
        // the remarks. Three different constants are three different lanes: the
        // field is hashed per lattice cell, so lanes one apart share nothing.
        var laneOne = b.Add("value", (0, 0f));
        var laneTwo = b.Add("value", (0, 1f));
        var laneThree = b.Add("value", (0, 2f));

        // How fast each lane is walked, in cells a second. The slowest takes
        // nearly two minutes to reach the next value it has not seen, which is
        // what makes the bass move like weather rather than like a bass line.
        var minutes = b.Add("math.mul", (1, 0.043f));
        var seconds = b.Add("math.mul", (1, 0.091f));
        var hours = b.Add("math.mul", (1, 0.0097f));

        var wander = b.Add("pattern.noise", (3, 1f));
        var flutter = b.Add("pattern.noise", (3, 1f));
        var tide = b.Add("pattern.noise", (3, 1f));

        b.Wire(clock, 0, minutes, 0)
         .Wire(clock, 0, seconds, 0)
         .Wire(clock, 0, hours, 0)

         .Wire(laneOne, 0, wander, 0)
         .Wire(laneOne, 0, wander, 1)
         .Wire(minutes, 0, wander, 2)

         .Wire(laneTwo, 0, flutter, 0)
         .Wire(laneTwo, 0, flutter, 1)
         .Wire(seconds, 0, flutter, 2)

         .Wire(laneThree, 0, tide, 0)
         .Wire(laneThree, 0, tide, 1)
         .Wire(hours, 0, tide, 2);

        b.Group("Three Random Voltages", clock, laneOne, laneTwo, laneThree,
            minutes, seconds, hours, wander, flutter, tide);

        // --- three quantisers ------------------------------------------------

        // D minor pentatonic over three octaves, split into three lists of
        // coprime length: eight notes for the pad, seven for the bell, five for
        // the root. 'rate' is how much of the list one full swing of the voltage
        // covers, so setting it to the length of the list is what makes the
        // whole scale reachable and nothing beyond it.
        var padSteps = b.Add("seq.notes", (1, 8f), (2, 1f), (3, 0.5f));
        StepsExtra.Set(padSteps,
        [
            new Step(57f), new Step(60f), new Step(62f), new Step(65f),
            new Step(67f), new Step(69f), new Step(72f), new Step(74f),
        ]);

        var bellSteps = b.Add("seq.notes", (1, 7f), (2, 1f), (3, 0.5f));
        StepsExtra.Set(bellSteps,
        [
            new Step(60f), new Step(62f), new Step(65f), new Step(67f),
            new Step(69f), new Step(72f), new Step(74f),
        ]);

        var rootSteps = b.Add("seq.notes", (1, 5f), (2, 1f), (3, 0.5f));
        StepsExtra.Set(rootSteps,
        [
            new Step(38f), new Step(43f), new Step(45f), new Step(41f), new Step(36f),
        ]);

        b.Wire(wander, 0, padSteps, 0)
         .Wire(flutter, 0, bellSteps, 0)
         .Wire(tide, 0, rootSteps, 0);

        b.Group("Three Quantisers", padSteps, bellSteps, rootSteps);

        // --- pad ---------------------------------------------------------------

        // Two sines a few cents apart, with the cents themselves on a sine
        // slower than either — so the beating between them speeds up and slows
        // down instead of sitting at one rate. The Chorus after them is what
        // makes the pair stereo: 'out' and 'wide' are swept in opposite
        // directions, which is a wider and cheaper answer than panning.
        var padNote = b.Add("audio.note");
        var detune = b.Add("osc.sine", (1, 0.0233f), (3, 7f));
        var padTwin = b.Add("audio.note");

        var padLower = b.Add("osc.sine", (3, 0.6f));
        var padUpper = b.Add("osc.sine", (3, 0.6f));
        var padPair = b.Add("math.add");

        // The swell, on a floor. A gate at 'gate length' one and 'shape' at its
        // longest is a hump rather than a switch, and the Remap under it is what
        // stops the hump reaching nothing — see the remarks.
        var padSwell = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.45f), (4, 1f));
        var padBreath = b.Add("osc.sine", (1, 0.0173f), (3, 0.2f), (4, 0.8f));
        var padLevel = b.Add("math.mul");
        var padVoiced = b.Add("math.mul");

        var thicken = b.Add(Chorus, (1, 0.19f), (2, 0.75f), (3, 0.6f));

        b.Wire(padSteps, 0, padNote, 0)
         .Wire(padNote, 1, padTwin, 0)
         .Wire(detune, 0, padTwin, 2)
         .Wire(padNote, 0, padLower, 1)
         .Wire(padTwin, 0, padUpper, 1)
         .Wire(padLower, 0, padPair, 0)
         .Wire(padUpper, 0, padPair, 1)

         .Wire(padSteps, 1, padSwell, 0)
         .Wire(padSwell, 0, padLevel, 0)
         .Wire(padBreath, 0, padLevel, 1)

         .Wire(padPair, 0, padVoiced, 0)
         .Wire(padLevel, 0, padVoiced, 1)
         .Wire(padVoiced, 0, thicken, 0);

        b.Group("Pad", padNote, detune, padTwin, padLower, padUpper, padPair,
            padSwell, padBreath, padLevel, padVoiced, thicken);

        // --- bell --------------------------------------------------------------

        // The one voice allowed to disappear, so the pad holds the patch up and
        // this is what happens in it. Through a Phaser slow enough to take most of
        // a minute a turn, so no two strikes of the same note have the same shape.
        // Panned by two sines at unrelated rates, which makes it wander across the
        // field rather than swing across it.
        var bellNote = b.Add("audio.note");
        var bell = b.Add("osc.sine", (3, 0.6f));
        var bellVoiced = b.Add("math.mul");

        var sweep = b.Add(Phaser, (1, 0.023f), (2, 0.85f), (3, 0.55f), (4, 0.7f));

        var bellDriftL = b.Add("osc.sine", (1, 0.0311f), (3, 0.4f), (4, 0.55f));
        var bellDriftR = b.Add("osc.sine", (1, 0.0419f), (2, 0.5f), (3, 0.4f), (4, 0.55f));
        var bellL = b.Add("math.mul");
        var bellR = b.Add("math.mul");

        b.Wire(bellSteps, 0, bellNote, 0)
         .Wire(bellNote, 0, bell, 1)
         .Wire(bell, 0, bellVoiced, 0)
         .Wire(bellSteps, 1, bellVoiced, 1)
         .Wire(bellVoiced, 0, sweep, 0)
         .Wire(sweep, 0, bellL, 0)
         .Wire(bellDriftL, 0, bellL, 1)
         .Wire(sweep, 0, bellR, 0)
         .Wire(bellDriftR, 0, bellR, 1);

        b.Group("Bell", bellNote, bell, bellVoiced, sweep, bellDriftL, bellDriftR, bellL, bellR);

        // --- drone -------------------------------------------------------------

        var rootNote = b.Add("audio.note");
        var subNote = b.Add("audio.note", (1, -1f));

        var droneTone = b.Add("osc.triangle", (3, 0.45f));
        var droneSub = b.Add("osc.sine", (3, 0.7f));
        var droneSum = b.Add("math.add");

        // Nearly always on. The floor is high and the breath on top of it is
        // shallow, because a root that comes and goes is a part rather than a
        // ground, and this is the ground.
        var droneHold = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.55f), (4, 1f));
        var droneBreath = b.Add("osc.sine", (1, 0.0133f), (3, 0.25f), (4, 0.75f));
        var droneLevel = b.Add("math.mul");
        var droneVoiced = b.Add("math.mul");

        // The one filter in the patch, on the one voice with harmonics worth
        // taking off. Its cutoff is the slowest voltage, so the bottom of the mix
        // opens and closes over minutes. 'low' rather than 'band' or 'high':
        // what a drone wants is less.
        var opening = b.Add("math.remap", (1, 0f), (2, 1f), (3, 130f), (4, 900f));
        var shaped = b.Add(Filter, (2, 0.35f));

        b.Wire(rootSteps, 0, rootNote, 0)
         .Wire(rootSteps, 0, subNote, 0)
         .Wire(rootNote, 0, droneTone, 1)
         .Wire(subNote, 0, droneSub, 1)
         .Wire(droneTone, 0, droneSum, 0)
         .Wire(droneSub, 0, droneSum, 1)

         .Wire(rootSteps, 1, droneHold, 0)
         .Wire(droneHold, 0, droneLevel, 0)
         .Wire(droneBreath, 0, droneLevel, 1)

         .Wire(droneSum, 0, droneVoiced, 0)
         .Wire(droneLevel, 0, droneVoiced, 1)
         .Wire(tide, 0, opening, 0)
         .Wire(droneVoiced, 0, shaped, 0)
         .Wire(opening, 0, shaped, 1);

        b.Group("Drone", rootNote, subNote, droneTone, droneSub, droneSum,
            droneHold, droneBreath, droneLevel, droneVoiced, opening, shaped);

        // --- air ---------------------------------------------------------------

        // The one voice that is not quantised: two sines sliding freely over the
        // voltages that quantise everything else, multiplied. A product of two
        // sines is their sum and their difference and nothing else, which is why
        // this sounds like a room rather than two oscillators.
        //
        // Both are kept low, which is a correction rather than a taste: the sum is
        // inharmonic, never gated off and sliding, so an octave higher it is one
        // thin whistle in the range the ear is most sensitive to. Held down, it
        // lands under a kilohertz and reads as air.
        //
        // Then a Flanger, on the one signal with enough going on for a comb of
        // notches to bite. Its feedback is negative, which puts the peaks where
        // the notches were: at this depth that is wind rather than a jet.
        var airOne = b.Add("math.remap", (1, 0f), (2, 1f), (3, 210f), (4, 610f));
        var airTwo = b.Add("math.remap", (1, 0f), (2, 1f), (3, 155f), (4, 440f));
        var glideOne = b.Add("osc.sine");
        var glideTwo = b.Add("osc.sine");
        var ring = b.Add("math.mul");

        var wind = b.Add(Flanger, (1, 0.037f), (2, 0.55f), (3, -0.3f), (4, 0.35f));

        // And a lid on it, because a flanger's comb puts peaks back wherever it
        // likes and the whole point of the register above is that nothing in
        // this voice is allowed to get shrill. Its cutoff rides a voltage like
        // everything else, so the lid is not a fixed one — but it is always
        // there, which is the difference between an effect and a safeguard.
        var airOpen = b.Add("math.remap", (1, 0f), (2, 1f), (3, 300f), (4, 800f));
        var soften = b.Add(Filter, (2, 0.1f));

        // And the thing that finally made this voice behave: it is allowed to not
        // be there. Every other voice is gated by a quantiser, and a flanger's comb
        // is eight tones within a decibel of each other — a permanent cluster at
        // seven hundred hertz over a pad that lives below three hundred is heard as
        // a whistle however far it is turned down. A Smoothstep off the slowest
        // voltage takes it away for whole minutes, which makes it an event.
        var presence = b.Add("math.smoothstep", (0, 0.32f), (1, 0.72f));
        var airPresent = b.Add("math.mul");

        var airDriftL = b.Add("osc.sine", (1, 0.0533f), (3, 0.45f), (4, 0.5f));
        var airDriftR = b.Add("osc.sine", (1, 0.0631f), (2, 0.5f), (3, 0.45f), (4, 0.5f));
        var airL = b.Add("math.mul");
        var airR = b.Add("math.mul");

        b.Wire(flutter, 0, airOne, 0)
         .Wire(wander, 0, airTwo, 0)
         .Wire(airOne, 0, glideOne, 1)
         .Wire(airTwo, 0, glideTwo, 1)
         .Wire(glideOne, 0, ring, 0)
         .Wire(glideTwo, 0, ring, 1)
         .Wire(ring, 0, wind, 0)
         .Wire(flutter, 0, airOpen, 0)
         .Wire(wind, 0, soften, 0)
         .Wire(airOpen, 0, soften, 1)
         .Wire(tide, 0, presence, 2)
         .Wire(soften, 0, airPresent, 0)
         .Wire(presence, 0, airPresent, 1)
         .Wire(airPresent, 0, airL, 0)
         .Wire(airDriftL, 0, airL, 1)
         .Wire(airPresent, 0, airR, 0)
         .Wire(airDriftR, 0, airR, 1);

        b.Group("Air", airOne, airTwo, glideOne, glideTwo, ring, wind, airOpen, soften,
            presence, airPresent, airDriftL, airDriftR, airL, airR);

        // --- the desk, and the two rooms ---------------------------------------

        var deskL = b.Add("math.mixer", (1, 0.95f), (3, 0.6f), (5, 0.7f), (7, 0.16f));
        var deskR = b.Add("math.mixer", (1, 0.95f), (3, 0.6f), (5, 0.7f), (7, 0.16f));

        // Two Delays rather than one, at times far enough apart not to be heard as
        // one echo and each on a different voltage, so the two sides pull apart
        // over minutes. A swept delay line interpolates rather than steps, so what
        // that does to the repeats is tape wow.
        var echoLeft = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.54f), (4, 0.68f));
        var echoRight = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.79f), (4, 0.93f));

        var repeatsL = b.Add("flyback.effects.delay", (2, 0.62f), (3, 0.4f));
        var repeatsR = b.Add("flyback.effects.delay", (2, 0.6f), (3, 0.4f));

        // The room, and it changes size. Two of them because one would put both
        // sides in the same place, and the sizes are offset so the tails are not
        // the same tail — a reverb is a bank of delays, and two banks a little
        // apart is what a room sounds like from a seat in it rather than from a
        // point in the middle.
        var roomSize = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.72f), (4, 0.98f));
        var roomWide = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.66f), (4, 0.92f));

        var hallL = b.Add("flyback.effects.reverb", (2, 0.86f), (3, 0.42f));
        var hallR = b.Add("flyback.effects.reverb", (2, 0.86f), (3, 0.42f));

        // No drive in front of these, unlike every other patch with a limiter in
        // it. Ambient has no transients to catch and nothing to gain by being
        // pushed into a wall; the Clamps are here because a Mixer sums, a reverb
        // adds a tail to what it sums, and four voices that each breathe on
        // their own will occasionally breathe in at once.
        var safeL = b.Add("math.clamp", (1, -1f), (2, 1f));
        var safeR = b.Add("math.clamp", (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.85f));

        b.Wire(thicken, 0, deskL, 0)
         .Wire(bellL, 0, deskL, 2)
         .Wire(shaped, 0, deskL, 4)
         .Wire(airL, 0, deskL, 6)

         .Wire(thicken, 1, deskR, 0)
         .Wire(bellR, 0, deskR, 2)
         .Wire(shaped, 0, deskR, 4)
         .Wire(airR, 0, deskR, 6)

         .Wire(wander, 0, echoLeft, 0)
         .Wire(flutter, 0, echoRight, 0)
         .Wire(tide, 0, roomSize, 0)
         .Wire(wander, 0, roomWide, 0)

         .Wire(deskL, 0, repeatsL, 0)
         .Wire(echoLeft, 0, repeatsL, 1)
         .Wire(deskR, 0, repeatsR, 0)
         .Wire(echoRight, 0, repeatsR, 1)

         .Wire(repeatsL, 0, hallL, 0)
         .Wire(roomSize, 0, hallL, 1)
         .Wire(repeatsR, 0, hallR, 0)
         .Wire(roomWide, 0, hallR, 1)

         .Wire(hallL, 0, safeL, 0)
         .Wire(hallR, 0, safeR, 0)
         .Wire(safeL, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(safeR, 0, output, NodeCatalog.OutputRightPort);

        b.Group("Desk & Rooms", deskL, deskR, echoLeft, echoRight, repeatsL, repeatsR,
            roomSize, roomWide, hallL, hallR, safeL, safeR);

        // --- the picture: geometry ---------------------------------------------

        // The only thing on the screen that moves at a steady rate, and it is a
        // rotation, which has nowhere to arrive. Everything else here is one of
        // the three voltages, so nothing in the frame is on its way anywhere in
        // particular.
        var creep = b.Add("math.mul", (1, 0.011f));
        var sway = b.Add("math.remap", (1, 0f), (2, 1f), (3, -0.6f), (4, 0.6f));
        var angle = b.Add("math.add");
        var turn = b.Add("space.rotate");

        var breathe = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.75f), (4, 1.45f));
        var zoom = b.Add("space.scale");

        // How many wedges, off the slowest voltage — so the symmetry of the
        // whole picture changes every couple of minutes, and changes to
        // somewhere it has not necessarily been.
        var wedges = b.Add("math.remap", (1, 0f), (2, 1f), (3, 2f), (4, 9f));
        var fold = b.Add("space.kaleidoscope");

        // The cloud is Noise read the ordinary way — per pixel, off the folded
        // plane, boiling on its own clock. The same module as the three
        // voltages, and the difference between a source and a texture is
        // entirely in what its x and y are patched to.
        var boil = b.Add("math.mul", (1, 0.035f));
        var cloud = b.Add("pattern.noise", (3, 1.6f));

        var depth = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.25f), (4, 0.85f));
        var bend = b.Add("space.warp");

        var spacing = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1.4f), (4, 3.6f));
        var swim = b.Add("math.mul", (1, 0.09f));
        var veil = b.Add("pattern.rings");

        // Wide edges, unlike every other preset that does this. A hard threshold
        // makes filaments and a soft one makes weather, and the difference is
        // where the two numbers are put.
        var haze = b.Add("math.smoothstep", (0, -0.55f), (1, 0.9f));

        b.Wire(clock, 0, creep, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(clock, 0, swim, 0)

         .Wire(tide, 0, sway, 0)
         .Wire(creep, 0, angle, 0)
         .Wire(sway, 0, angle, 1)
         .Wire(angle, 0, turn, 2)

         .Wire(wander, 0, breathe, 0)
         .Wire(turn, 0, zoom, 0)
         .Wire(turn, 1, zoom, 1)
         .Wire(breathe, 0, zoom, 2)

         .Wire(tide, 0, wedges, 0)
         .Wire(zoom, 0, fold, 0)
         .Wire(zoom, 1, fold, 1)
         .Wire(wedges, 0, fold, 2)

         .Wire(fold, 0, cloud, 0)
         .Wire(fold, 1, cloud, 1)
         .Wire(boil, 0, cloud, 2)

         .Wire(flutter, 0, depth, 0)
         .Wire(fold, 0, bend, 0)
         .Wire(fold, 1, bend, 1)
         .Wire(cloud, 0, bend, 2)
         .Wire(depth, 0, bend, 3)

         .Wire(padSteps, 2, spacing, 0)
         .Wire(bend, 0, veil, 0)
         .Wire(bend, 1, veil, 1)
         .Wire(spacing, 0, veil, 2)
         .Wire(swim, 0, veil, 3)
         .Wire(veil, 0, haze, 2);

        b.Group("Picture: Geometry", creep, sway, angle, turn, breathe, zoom, wedges, fold,
            boil, cloud, depth, bend, spacing, swim, veil, haze);

        // --- the picture: color -------------------------------------------------

        // Here for 'radius', which is the one Coordinates output nothing is
        // normalled to, and the only thing in the patch that knows where the
        // edge of the frame is.
        var coord = b.Add("coord");
        var falloff = b.Add("math.remap", (1, 0f), (2, 2.2f), (3, 1f), (4, 0.3f));

        var glow = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.7f), (4, 1.35f));
        var lit = b.Add("math.mul");
        var shaded = b.Add("math.mul");
        var visible = b.Add("math.clamp", (1, 0f), (2, 1f));

        // Hue off the cloud and the slowest voltage together, so the palette
        // moves across the frame and drifts as a whole at the same time, and the
        // creep under both means it never settles even where the two do.
        var spread = b.Add("math.mul", (1, 0.55f));
        var season = b.Add("math.mul", (1, 0.4f));
        var blend = b.Add("math.add");
        var slide = b.Add("math.add");
        var hue = b.Add("math.fract");

        var wash = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.3f), (4, 0.75f));

        var fresh = b.Add("color.hsv");

        b.Wire(coord, 2, falloff, 0)

         .Wire(padSteps, 1, glow, 0)
         .Wire(haze, 0, lit, 0)
         .Wire(glow, 0, lit, 1)
         .Wire(lit, 0, shaded, 0)
         .Wire(falloff, 0, shaded, 1)
         .Wire(shaded, 0, visible, 0)

         .Wire(cloud, 0, spread, 0)
         .Wire(tide, 0, season, 0)
         .Wire(spread, 0, blend, 0)
         .Wire(season, 0, blend, 1)
         .Wire(blend, 0, slide, 0)
         .Wire(creep, 0, slide, 1)
         .Wire(slide, 0, hue, 0)

         .Wire(flutter, 0, wash, 0)

         .Wire(hue, 0, fresh, 0)
         .Wire(wash, 0, fresh, 1)
         .Wire(visible, 0, fresh, 2);

        b.Group("Picture: Color", coord, falloff, glow, lit, shaded, visible,
            spread, season, blend, slide, hue, wash, fresh);

        // --- the picture: memory -------------------------------------------------

        // Blended rather than maximised, which is what a still picture needs:
        // Maximum keeps whatever was brightest and reads as a streak, where a Blend
        // lets the frame forget.
        //
        // How much it forgets is the fastest of the three voltages, so the picture
        // is sharp for a while and long-exposed for a while. The Feedback module
        // rather than this plugin's Delay, for the reason the plugin exists to
        // explain: a delay line has no per-pixel past.
        var adrift = b.Add("space.scale", (2, 1.008f));
        var aturn = b.Add("space.rotate", (2, 0.0035f));
        var previous = b.Add("feedback");
        var memory = b.Add("color.gain", (1, 0.985f), (2, 0f));

        var settle = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.05f), (4, 0.2f));
        var combine = b.Add("color.mix");

        b.Wire(adrift, 0, aturn, 0)
         .Wire(adrift, 1, aturn, 1)
         .Wire(aturn, 0, previous, 0)
         .Wire(aturn, 1, previous, 1)
         .Wire(previous, 0, memory, 0)

         .Wire(wander, 0, settle, 0)
         .Wire(memory, 0, combine, 0)
         .Wire(fresh, 0, combine, 1)
         .Wire(settle, 0, combine, 2)
         .Wire(combine, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Picture: Memory", adrift, aturn, previous, memory, settle, combine);

        return b.Build();
    }
}
