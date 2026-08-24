using Flyback.Core.Graph;

namespace Flyback.Plugins.Space;

/// <summary>
/// A generative patch with no clock in it, played into the two effects this
/// plugin is for. Three random voltages read out of a noise field choose the
/// notes, open the voices and move the picture; nothing anywhere is counting
/// beats, so there is no bar for any of it to come round on.
/// </summary>
/// <remarks>
/// It is the second preset here to reach across a plugin boundary — the moving
/// effects are in <c>Flyback.Plugins.Modulation</c> and the shaping ones in
/// <c>Flyback.Plugins.Timbre</c> — which is allowed and is not free. A preset is
/// handed the catalogue when it is picked rather than when it is registered, so
/// this is where a missing plugin shows up, and the checks below are only so
/// that it says which one.
/// <para>
/// The module that makes the generative half work is Noise, used as a source
/// rather than as a texture. Its x and y are held still by a Value, so what is
/// left moving is z — and a line through a noise field, walked slowly, is
/// exactly the smooth random voltage a modular patch would take off a
/// sample-and-hold with a lag on it. Three of them are read here at three
/// speeds, from three lanes far enough apart in the field to be unrelated.
/// </para>
/// <para>
/// Holding x and y still is not a detail. Left alone they are normalled to
/// Coordinates, and a control voltage that varies per pixel is not one: the
/// speakers would be at the middle of the picture hearing one note while the
/// screen showed a field of every other note at once. That is a fine thing to do
/// on purpose — the Chromatic preset does it — and it is the wrong thing here,
/// where both sinks have to agree about what is playing.
/// </para>
/// <para>
/// The sequencers are quantisers rather than sequencers. A step sequencer's 'in'
/// is a domain like an oscillator's, so it plays whatever is patched into it:
/// give it a clock and it plays a tune, and give it a random voltage and it
/// walks its list at the voltage's own pace, forwards and backwards, holding
/// wherever the voltage holds. What is on the list stops being a rhythm and
/// becomes a scale — the notes it is allowed to play — and 'rate' stops being a
/// tempo and becomes how much of the list one full swing of the voltage covers.
/// Three of them, on three lists of coprime length driven by three unrelated
/// voltages, is a chord that reshuffles itself and never lands the same way
/// twice.
/// </para>
/// <para>
/// The gates come free with that. Each voice's 'gate length' is one and its
/// 'shape' is as long as it can be, so what would be a note's attack and release
/// under a clock becomes a swell lasting as long as the voltage takes to cross
/// the step — tens of seconds, and never the same twice either. Each swell sits
/// on a floor, because a gate that reaches nothing is a hole rather than a
/// breath; only the bell is allowed to go away entirely.
/// </para>
/// <para>
/// Every effect in the chain is modulated by the same three voltages rather than
/// by a rate of its own, which is the whole reason to build it this way: the
/// filter opens on the slowest of them, the delay times drift on two different
/// ones so the two sides pull apart and come back, and the room changes size on
/// the slowest. Nothing about the treatment is fixed either.
/// </para>
/// <para>
/// One thing deliberately not done: none of the three moving effects has its
/// 'lfo' patched into the picture, tempting as that is. A module is resolved
/// whole, so reaching for an output at the end of a chain compiles everything
/// upstream of all of its inputs — and at the end of these chains that is the
/// entire voice. The Whole rack preset says the same thing at more length, and
/// pays for it in the same currency: the picture here costs 232 ops and would
/// cost several times that.
/// </para>
/// <para>
/// Nothing repeats. The noise is hashed off an integer lattice with no period
/// until the lattice runs out, which at the slowest of the three speeds is some
/// thousands of years; the level and pan oscillators are the only strictly
/// periodic things in the patch, and their rates share no factor above a
/// ten-thousandth, so even those do not come round together inside three hours.
/// The picture inherits all of it and adds a feedback loop whose blend is itself
/// one of the voltages, so how long the screen remembers drifts as well.
/// </para>
/// </remarks>
internal static class SlowWeatherPreset
{
    public const string Name = "Slow weather";

    private const string Modulation = "flyback.mod";
    private const string Timbre = "flyback.timbre";

    private const string Chorus = "flyback.mod.chorus";
    private const string Phaser = "flyback.mod.phaser";
    private const string Flanger = "flyback.mod.flanger";
    private const string Filter = "flyback.timbre.filter";

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Modulation))
            throw new InvalidOperationException(
                $"it needs the Modulation plugin ({Modulation}), which is not installed.");

        if (!modules.HasProvider(Timbre))
            throw new InvalidOperationException(
                $"it needs the Filter and fold plugin ({Timbre}), which is not installed.");

        var b = new PatchBuilder(modules);

        // --- three random voltages -------------------------------------------

        var clock = b.Add("time", 40, 1500);

        // Where in the field each voltage is read. A knob and two wires, and it
        // is the whole of what turns Noise from a texture into a source — see
        // the remarks. Three different constants are three different lanes: the
        // field is hashed per lattice cell, so lanes one apart share nothing.
        var laneOne = b.Add("value", 40, 1180, (0, 0f));
        var laneTwo = b.Add("value", 40, 1780, (0, 1f));
        var laneThree = b.Add("value", 40, 2060, (0, 2f));

        // How fast each lane is walked, in cells a second. The slowest takes
        // nearly two minutes to reach the next value it has not seen, which is
        // what makes the bass move like weather rather than like a bass line.
        var minutes = b.Add("math.mul", 250, 1300, (1, 0.043f));
        var seconds = b.Add("math.mul", 250, 1700, (1, 0.091f));
        var hours = b.Add("math.mul", 250, 2060, (1, 0.0097f));

        var wander = b.Add("pattern.noise", 480, 1180, (3, 1f));
        var flutter = b.Add("pattern.noise", 480, 1620, (3, 1f));
        var tide = b.Add("pattern.noise", 480, 2060, (3, 1f));

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

        // --- three quantisers ------------------------------------------------

        // D minor pentatonic over three octaves, split into three lists of
        // coprime length: eight notes for the pad, seven for the bell, five for
        // the root. 'rate' is how much of the list one full swing of the voltage
        // covers, so setting it to the length of the list is what makes the
        // whole scale reachable and nothing beyond it.
        var padSteps = b.Add("seq.notes", 730, 1180, (1, 8f), (2, 1f), (3, 0.5f));
        padSteps.Steps =
        [
            new Step(57f), new Step(60f), new Step(62f), new Step(65f),
            new Step(67f), new Step(69f), new Step(72f), new Step(74f),
        ];

        var bellSteps = b.Add("seq.notes", 730, 1620, (1, 7f), (2, 1f), (3, 0.5f));
        bellSteps.Steps =
        [
            new Step(60f), new Step(62f), new Step(65f), new Step(67f),
            new Step(69f), new Step(72f), new Step(74f),
        ];

        var rootSteps = b.Add("seq.notes", 730, 2060, (1, 5f), (2, 1f), (3, 0.5f));
        rootSteps.Steps =
        [
            new Step(38f), new Step(43f), new Step(45f), new Step(41f), new Step(36f),
        ];

        b.Wire(wander, 0, padSteps, 0)
         .Wire(flutter, 0, bellSteps, 0)
         .Wire(tide, 0, rootSteps, 0);

        // --- pad ---------------------------------------------------------------

        // Two sines a few cents apart, with the cents themselves on a sine
        // slower than either — so the beating between them speeds up and slows
        // down instead of sitting at one rate. The Chorus after them is what
        // makes the pair stereo: 'out' and 'wide' are swept in opposite
        // directions, which is a wider and cheaper answer than panning.
        var padNote = b.Add("audio.note", 960, 1180);
        var detune = b.Add("osc.sine", 960, 1400, (1, 0.0233f), (3, 7f));
        var padTwin = b.Add("audio.note", 1190, 1400);

        var padLower = b.Add("osc.sine", 1420, 1120, (3, 0.6f));
        var padUpper = b.Add("osc.sine", 1420, 1320, (3, 0.6f));
        var padPair = b.Add("math.add", 1650, 1220);

        // The swell, on a floor. A gate at 'gate length' one and 'shape' at its
        // longest is a hump rather than a switch, and the Remap under it is what
        // stops the hump reaching nothing — see the remarks.
        var padSwell = b.Add("math.remap", 960, 900, (1, 0f), (2, 1f), (3, 0.45f), (4, 1f));
        var padBreath = b.Add("osc.sine", 960, 700, (1, 0.0173f), (3, 0.2f), (4, 0.8f));
        var padLevel = b.Add("math.mul", 1190, 820);
        var padVoiced = b.Add("math.mul", 1880, 1220);

        var thicken = b.Add(Chorus, 2110, 1180, (1, 0.19f), (2, 0.75f), (3, 0.6f));

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

        // --- bell --------------------------------------------------------------

        // The one voice allowed to disappear, so that the pad holds the patch up
        // and this is what happens in it. Through a Phaser slow enough to take
        // most of a minute a turn, which is what a bell wants: the notches move
        // while the note rings, so no two strikes of the same note have the same
        // shape. Panned afterwards by two sines at unrelated rates rather than by
        // one and its opposite, which is what makes it wander across the field
        // instead of swinging across it.
        var bellNote = b.Add("audio.note", 960, 1620);
        var bell = b.Add("osc.sine", 1190, 1620, (3, 0.6f));
        var bellVoiced = b.Add("math.mul", 1420, 1620);

        var sweep = b.Add(Phaser, 1650, 1580, (1, 0.023f), (2, 0.85f), (3, 0.55f), (4, 0.7f));

        var bellDriftL = b.Add("osc.sine", 1650, 1840, (1, 0.0311f), (3, 0.4f), (4, 0.55f));
        var bellDriftR = b.Add("osc.sine", 1650, 1980, (1, 0.0419f), (2, 0.5f), (3, 0.4f), (4, 0.55f));
        var bellL = b.Add("math.mul", 1880, 1600);
        var bellR = b.Add("math.mul", 1880, 1780);

        b.Wire(bellSteps, 0, bellNote, 0)
         .Wire(bellNote, 0, bell, 1)
         .Wire(bell, 0, bellVoiced, 0)
         .Wire(bellSteps, 1, bellVoiced, 1)
         .Wire(bellVoiced, 0, sweep, 0)
         .Wire(sweep, 0, bellL, 0)
         .Wire(bellDriftL, 0, bellL, 1)
         .Wire(sweep, 0, bellR, 0)
         .Wire(bellDriftR, 0, bellR, 1);

        // --- drone -------------------------------------------------------------

        var rootNote = b.Add("audio.note", 960, 2260);
        var subNote = b.Add("audio.note", 960, 2460, (1, -1f));

        var droneTone = b.Add("osc.triangle", 1190, 2260, (3, 0.45f));
        var droneSub = b.Add("osc.sine", 1190, 2460, (3, 0.7f));
        var droneSum = b.Add("math.add", 1420, 2340);

        // Nearly always on. The floor is high and the breath on top of it is
        // shallow, because a root that comes and goes is a part rather than a
        // ground, and this is the ground.
        var droneHold = b.Add("math.remap", 960, 2660, (1, 0f), (2, 1f), (3, 0.55f), (4, 1f));
        var droneBreath = b.Add("osc.sine", 960, 2820, (1, 0.0133f), (3, 0.25f), (4, 0.75f));
        var droneLevel = b.Add("math.mul", 1190, 2700);
        var droneVoiced = b.Add("math.mul", 1650, 2340);

        // The one filter in the patch, and it is on the one voice with harmonics
        // worth taking off. Its cutoff is the slowest voltage, so the bottom of
        // the mix opens and closes over minutes and never at a rate anything
        // else in the patch shares. 'low' rather than 'band' or 'high': what a
        // drone wants is less, and the other two responses are what the module
        // hands out for free.
        var opening = b.Add("math.remap", 1420, 2560, (1, 0f), (2, 1f), (3, 130f), (4, 900f));
        var shaped = b.Add(Filter, 1880, 2300, (2, 0.35f));

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

        // --- air ---------------------------------------------------------------

        // The one voice that is not quantised at all: two sines sliding freely
        // over the voltages that quantise everything else, multiplied together.
        // A product of two sines is their sum and their difference and nothing
        // else, so what comes out is a pair of tones moving in opposite
        // directions from a pair moving in the same one — which is why this
        // sounds like a room rather than like two oscillators.
        //
        // Both are kept low, and that is a correction rather than a taste. The
        // sum is the one thing here nothing else in the patch controls: it is
        // inharmonic, it is never gated off, and it slides, so put the pair an
        // octave higher and what the ear picks out of an otherwise still mix is
        // one thin whistle wandering about in the range it is most sensitive to.
        // Held down here the sum lands under a kilohertz and reads as air.
        //
        // Then a Flanger, on the one signal in the patch with enough going on
        // for a comb of notches to have something to bite. Its feedback is
        // negative, which puts the peaks where the notches were: at this depth
        // and this rate that is wind rather than a jet.
        var airOne = b.Add("math.remap", 960, 3020, (1, 0f), (2, 1f), (3, 210f), (4, 610f));
        var airTwo = b.Add("math.remap", 960, 3180, (1, 0f), (2, 1f), (3, 155f), (4, 440f));
        var glideOne = b.Add("osc.sine", 1190, 3020);
        var glideTwo = b.Add("osc.sine", 1190, 3180);
        var ring = b.Add("math.mul", 1420, 3100);

        var wind = b.Add(Flanger, 1650, 3060, (1, 0.037f), (2, 0.55f), (3, -0.3f), (4, 0.35f));

        // And a lid on it, because a flanger's comb puts peaks back wherever it
        // likes and the whole point of the register above is that nothing in
        // this voice is allowed to get shrill. Its cutoff rides a voltage like
        // everything else, so the lid is not a fixed one — but it is always
        // there, which is the difference between an effect and a safeguard.
        var airOpen = b.Add("math.remap", 1420, 3320, (1, 0f), (2, 1f), (3, 300f), (4, 800f));
        var soften = b.Add(Filter, 1880, 3020, (2, 0.1f));

        // And the thing that finally made this voice behave: it is allowed to
        // not be there. Every other voice is gated by a quantiser, and this one
        // had nothing but two pan sines that never quite reach zero — so a
        // flanger's comb, which is eight tones within a decibel of each other,
        // sat in the mix permanently. Against a pad and a drone that live below
        // three hundred hertz, a permanent cluster at seven hundred is not heard
        // as air; it is heard as a whistle, and no amount of turning it down
        // stops it being the thing the ear finds. A Smoothstep off the slowest
        // voltage takes it away entirely for whole minutes at a time, which is
        // what makes it an event rather than a fixture.
        var presence = b.Add("math.smoothstep", 2110, 3320, (0, 0.32f), (1, 0.72f));
        var airPresent = b.Add("math.mul", 2340, 3060);

        var airDriftL = b.Add("osc.sine", 2340, 3320, (1, 0.0533f), (3, 0.45f), (4, 0.5f));
        var airDriftR = b.Add("osc.sine", 2340, 3460, (1, 0.0631f), (2, 0.5f), (3, 0.45f), (4, 0.5f));
        var airL = b.Add("math.mul", 2570, 3060);
        var airR = b.Add("math.mul", 2570, 3240);

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

        // --- the desk, and the two rooms ---------------------------------------

        var deskL = b.Add("math.mixer", 2340, 1900, (1, 0.95f), (3, 0.6f), (5, 0.7f), (7, 0.16f));
        var deskR = b.Add("math.mixer", 2340, 2500, (1, 0.95f), (3, 0.6f), (5, 0.7f), (7, 0.16f));

        // Two Delays rather than one, at times far enough apart not to be heard
        // as one echo, and each one's time on a different voltage — so the two
        // sides pull apart and come back together over minutes. A swept delay
        // line interpolates rather than steps, so what that does to the repeats
        // is tape wow: the pitch of an echo is never quite the pitch it was
        // played at.
        var echoLeft = b.Add("math.remap", 2340, 1420, (1, 0f), (2, 1f), (3, 0.54f), (4, 0.68f));
        var echoRight = b.Add("math.remap", 2340, 3660, (1, 0f), (2, 1f), (3, 0.79f), (4, 0.93f));

        var repeatsL = b.Add("flyback.space.delay", 2570, 1900, (2, 0.62f), (3, 0.4f));
        var repeatsR = b.Add("flyback.space.delay", 2570, 2500, (2, 0.6f), (3, 0.4f));

        // The room, and it changes size. Two of them because one would put both
        // sides in the same place, and the sizes are offset so the tails are not
        // the same tail — a reverb is a bank of delays, and two banks a little
        // apart is what a room sounds like from a seat in it rather than from a
        // point in the middle.
        var roomSize = b.Add("math.remap", 2340, 1620, (1, 0f), (2, 1f), (3, 0.72f), (4, 0.98f));
        var roomWide = b.Add("math.remap", 2340, 3860, (1, 0f), (2, 1f), (3, 0.66f), (4, 0.92f));

        var hallL = b.Add("flyback.space.reverb", 2800, 1900, (2, 0.86f), (3, 0.42f));
        var hallR = b.Add("flyback.space.reverb", 2800, 2500, (2, 0.86f), (3, 0.42f));

        // No drive in front of these, unlike every other patch with a limiter in
        // it. Ambient has no transients to catch and nothing to gain by being
        // pushed into a wall; the Clamps are here because a Mixer sums, a reverb
        // adds a tail to what it sums, and four voices that each breathe on
        // their own will occasionally breathe in at once.
        var safeL = b.Add("math.clamp", 3030, 1900, (1, -1f), (2, 1f));
        var safeR = b.Add("math.clamp", 3030, 2500, (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, 3720, 1300, (NodeCatalog.OutputGainPort, 0.85f));

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

        // --- the picture: geometry ---------------------------------------------

        // The only thing on the screen that moves at a steady rate, and it is a
        // rotation, which has nowhere to arrive. Everything else here is one of
        // the three voltages, so nothing in the frame is on its way anywhere in
        // particular.
        var creep = b.Add("math.mul", 250, 120, (1, 0.011f));
        var sway = b.Add("math.remap", 250, 280, (1, 0f), (2, 1f), (3, -0.6f), (4, 0.6f));
        var angle = b.Add("math.add", 480, 180);
        var turn = b.Add("space.rotate", 710, 140);

        var breathe = b.Add("math.remap", 480, 380, (1, 0f), (2, 1f), (3, 0.75f), (4, 1.45f));
        var zoom = b.Add("space.scale", 940, 160);

        // How many wedges, off the slowest voltage — so the symmetry of the
        // whole picture changes every couple of minutes, and changes to
        // somewhere it has not necessarily been.
        var wedges = b.Add("math.remap", 710, 400, (1, 0f), (2, 1f), (3, 2f), (4, 9f));
        var fold = b.Add("space.kaleidoscope", 1170, 180);

        // The cloud is Noise read the ordinary way — per pixel, off the folded
        // plane, boiling on its own clock. The same module as the three
        // voltages, and the difference between a source and a texture is
        // entirely in what its x and y are patched to.
        var boil = b.Add("math.mul", 250, 460, (1, 0.035f));
        var cloud = b.Add("pattern.noise", 1400, 400, (3, 1.6f));

        var depth = b.Add("math.remap", 1400, 620, (1, 0f), (2, 1f), (3, 0.25f), (4, 0.85f));
        var bend = b.Add("space.warp", 1630, 200);

        var spacing = b.Add("math.remap", 1400, 780, (1, 0f), (2, 1f), (3, 1.4f), (4, 3.6f));
        var swim = b.Add("math.mul", 250, 620, (1, 0.09f));
        var veil = b.Add("pattern.rings", 1860, 220);

        // Wide edges, unlike every other preset that does this. A hard threshold
        // makes filaments and a soft one makes weather, and the difference is
        // where the two numbers are put.
        var haze = b.Add("math.smoothstep", 2090, 260, (0, -0.55f), (1, 0.9f));

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

        // --- the picture: color -------------------------------------------------

        // Here for 'radius', which is the one Coordinates output nothing is
        // normalled to, and the only thing in the patch that knows where the
        // edge of the frame is.
        var coord = b.Add("coord", 40, 700);
        var falloff = b.Add("math.remap", 250, 700, (1, 0f), (2, 2.2f), (3, 1f), (4, 0.3f));

        var glow = b.Add("math.remap", 1860, 560, (1, 0f), (2, 1f), (3, 0.7f), (4, 1.35f));
        var lit = b.Add("math.mul", 2320, 300);
        var shaded = b.Add("math.mul", 2550, 300);
        var visible = b.Add("math.clamp", 2780, 300, (1, 0f), (2, 1f));

        // Hue off the cloud and the slowest voltage together, so the palette
        // moves across the frame and drifts as a whole at the same time, and the
        // creep under both means it never settles even where the two do.
        var spread = b.Add("math.mul", 1630, 940, (1, 0.55f));
        var season = b.Add("math.mul", 1630, 1100, (1, 0.4f));
        var blend = b.Add("math.add", 1860, 940);
        var slide = b.Add("math.add", 2090, 940);
        var hue = b.Add("math.fract", 2320, 940);

        var wash = b.Add("math.remap", 2320, 780, (1, 0f), (2, 1f), (3, 0.3f), (4, 0.75f));

        var fresh = b.Add("color.hsv", 2550, 620);

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

        // --- the picture: memory -------------------------------------------------

        // Blended rather than maximised, which is the opposite choice from every
        // other feedback patch and is what a still picture needs: Maximum keeps
        // whatever was brightest and reads as a streak, and a Blend lets the
        // frame forget, so what is on screen is an average of the last several
        // seconds rather than a smear of everything since it started.
        //
        // How much it forgets is the fastest of the three voltages, so the
        // picture is sharp for a while and long-exposed for a while and there is
        // no telling when it changes over. It is the Feedback module rather than
        // this plugin's Delay, for the reason the plugin exists to explain: a
        // delay line has no per-pixel past, and a picture with a memory needs the
        // one module that does.
        var adrift = b.Add("space.scale", 1170, 1240, (2, 1.008f));
        var aturn = b.Add("space.rotate", 1400, 1240, (2, 0.0035f));
        var previous = b.Add("feedback", 1630, 1240);
        var memory = b.Add("color.gain", 1860, 1240, (1, 0.985f), (2, 0f));

        var settle = b.Add("math.remap", 1860, 1440, (1, 0f), (2, 1f), (3, 0.05f), (4, 0.2f));
        var combine = b.Add("color.mix", 2780, 900);

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

        return b.Patch;
    }
}
