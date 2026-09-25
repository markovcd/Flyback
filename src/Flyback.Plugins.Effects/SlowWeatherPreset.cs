using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A clockless generative patch with five feedback loops and six panel knobs.
/// Random voltages from a noise field pick the notes, open the voices and move
/// the picture; the loops (a self-bending drone, a darkening echo, two ducking
/// followers and a picture steered by last frame's light) make it evolve. Four
/// visitors come and go and mark the picture while they stay.
/// </summary>
/// <remarks>
/// A loop is one evaluation of delay (ADR-0075), and each has a gain under one
/// that its comment points to. The voices are sines, a triangle and a dark pluck,
/// the weather is filtered pink noise, and the echo and room darken as they ring,
/// so the spectrum tilts down about 3 dB an octave.
/// <para>
/// The six knobs (ADR-0086) ride on top of what the wanders already drive rather
/// than replacing it: Echo Level and Reverb Space scale the send and the rooms'
/// mix, Visual Warp and Trails Spin scale the warp on the rings and turn the
/// trails, Chime Decay stretches the music box's string, and Color Shift offsets
/// the hue.
/// </para>
/// </remarks>
internal sealed class SlowWeatherPreset : PresetBench
{
    public const string Name = "Slow weather";

    /// <summary>
    /// The plugin the voices are borrowed from. The effects live beside this
    /// preset; Wander and Hiss are in Voice, so this is a patch that reaches
    /// across a boundary and has to say so when the other plugin is not there.
    /// Filter, Noise and Slew are the engine's own (ADR-0128), so this preset
    /// reaches no boundary for them.
    /// </summary>
    private const string Voice = "flyback.voice";

    private const string ChorusType = "flyback.effects.chorus";

    private const string PhaserType = "flyback.effects.phaser";

    private const string DelayType = NodeCatalog.DelayTypeId;

    private const string ReverbType = NodeCatalog.ReverbTypeId;

    private const string FilterType = NodeCatalog.FilterTypeId;

    private const string SlewType = NodeCatalog.SlewTypeId;

    private const string NoiseType = NodeCatalog.NoiseTypeId;

    /// <summary>The outputs read by number below, named so a wire says which.</summary>
    private const int Hz = 0;

    private const int NoteNumber = 1;

    private const int Gate = 1;

    private const int Index = 2;

    private const int FilterLow = 0;

    private const int ChorusWide = 1;

    private const int ReverbWide = 1;

    private const int BusLeft = 2;

    private const int BusRight = 3;

    private const int Tail = 1;

    private const int Held = 2;

    /// <summary>
    /// How much of the last evaluation an envelope follower keeps. At the
    /// oversampled rate this is a time constant of about fifty milliseconds,
    /// which is enough to take the waveform out of the reading and leave the
    /// swell; the Slew after each follower is what makes the reading slow.
    /// </summary>
    private const float Kept = 0.9999f;

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        return new SlowWeatherPreset(modules).Assemble();
    }

    private SlowWeatherPreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    /// <summary>
    /// A Wander held against a list of notes. 'rate' is how much of the list one
    /// full swing of the voltage covers, so setting it to the length of the list
    /// is what makes the whole scale reachable and nothing beyond it. The gate is
    /// at its longest and softest, which makes it a hump a note rather than a
    /// switch: the voices below use it as their swell.
    /// </summary>
    private NodeInstance Quantized(NodeInstance voltage, float[] notes, int from = 0, float gateLength = 1f)
    {
        var steps = b.Add("seq.notes", (1, notes.Length), (2, gateLength), (3, 0.5f));
        StepsExtra.Set(steps, [.. notes.Select(n => new Step(n))]);
        b.Wire(voltage, from, steps, 0);
        return steps;
    }

    /// <summary>A panel knob's own value, mapped from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private NodeInstance Knob(PatchControl knob, float low, float high)
    {
        var node = b.Add("math.add");
        Follows(node, 0, knob, low, high);
        return node;
    }

    /// <summary>
    /// Whether a visitor is here: nought while its own Wander is under
    /// <paramref name="from"/>, one over <paramref name="to"/>. Read the Fade's
    /// gate; the same number opens the voice and marks the picture.
    /// </summary>
    private NodeInstance Presence(float rate, float seed, float from, float to) =>
        Enters(Wander(rate, seed), from, to);

    /// <summary>
    /// A Noise's held value: a new number from -1 to 1 <paramref name="rate"/>
    /// times a second, on the same edges as a Stroke of the same rate.
    /// </summary>
    private NodeInstance Dice(float rate, float seed, float amp = 1f, float bias = 0f) =>
        b.Add(NoiseType, (1, rate), (2, seed), (3, amp), (4, bias));

    /// <summary>A Desk channel's level socket, for a channel whose level is a wire.</summary>
    private static int LevelOf(int channel) => (channel - 1) * 3 + 2;

    /// <summary>
    /// A level, followed. The loop is an integrator on the size of the signal —
    /// each evaluation keeps most of what it held and adds a sliver of what
    /// arrived — and the wire back into it is the one that carries the evaluation
    /// before. The Slew is what makes the reading musical: it catches a swell in
    /// under a second and lets go of it over several, so whatever it drives is
    /// pushed quickly and comes back slowly.
    /// </summary>
    private NodeInstance Followed(NodeInstance signal, int from, float rise, float fall)
    {
        var kept = b.Add("math.mul", (1, Kept));
        var follow = Sum(kept, Times(Size(signal, from), 1f - Kept));
        b.Wire(follow, 0, kept, 0);

        var settled = b.Add(SlewType, (1, rise), (2, fall));
        b.Wire(follow, 0, settled, 0);
        return settled;
    }

    /// <summary>One minus a scaled reading, held above a floor: what a follower turns a level down by.</summary>
    private NodeInstance Ducked(NodeInstance reading, float by, float floor)
    {
        var duck = b.Add("math.clamp", (1, floor), (2, 1f));
        b.Wire(From(1f, Times(reading, by)), 0, duck, 0);
        return duck;
    }

    private Patch Assemble()
    {
        b.Patch.Length = 1800; // 30 minutes.

        // --- the panel ---------------------------------------------------------

        var echoLevel = Panel("Echo Level", 0.5f);
        var chimeDecay = Panel("Chime Decay", 0.5f);
        var reverbSpace = Panel("Reverb Space", 0.5f);
        var visualWarp = Panel("Visual Warp", 0.5f);
        var colorShift = Panel("Color Shift", 0.5f);
        var trailsSpin = Panel("Trails Spin", 0.5f);

        // --- four random voltages --------------------------------------------

        // Four Wanders, each with a seed of its own: the engine's noise walked
        // along one line, so it is a source rather than a texture, and the same
        // number on the screen as in the speakers. The rate is how fast the line
        // is walked, in values a second; the slowest takes nearly two minutes to
        // reach a value it has not seen, which is what makes the bass move like
        // weather rather than like a bass line.
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var tide = Wander(0.0097f, 2f);
        var wander = Wander(0.043f, 0f);
        var flutter = Wander(0.091f, 1f);
        var weather = Wander(0.019f, 3f);

        Box("Four Random Voltages");

        // --- three quantisers ------------------------------------------------

        // D minor pentatonic over three octaves, split into three lists of
        // coprime length: eight notes for the pad, seven for the bell, five for
        // the root.
        var padSteps = Quantized(wander, [57f, 60f, 62f, 65f, 67f, 69f, 72f, 74f]);
        var bellSteps = Quantized(flutter, [60f, 62f, 65f, 67f, 69f, 72f, 74f], gateLength: 0.6188406f);
        var rootSteps = Quantized(tide, [38f, 43f, 45f, 41f, 36f]);

        Box("Three Quantisers");

        // --- ground ----------------------------------------------------------

        // The first loop. A sine whose phase is pushed by its own last sample,
        // which is the feedback of a classic FM operator: at nothing it is a sine,
        // and as the index rises the wave leans over into something like a saw,
        // harmonic by harmonic and with no edge anywhere. The index is a voltage,
        // so the drone's timbre moves the way its pitch does. It is held well
        // under a quarter of a turn, which is where this loop stops being a
        // waveform and starts being chaos.
        var rootNote = Through("audio.note", rootSteps);
        var subNote = b.Add("audio.note", (1, -1f));
        var groundIndex = Span(weather, 0f, 1f, 0.02f, 0.16f);
        var ground = b.Add("osc.sine");
        var bite = b.Add("math.mul");
        var sub = b.Add("osc.sine", (3, 0.45f));

        b.Wire(rootSteps, 0, subNote, 0)
         .Wire(rootNote, Hz, ground, 1)
         .Wire(ground, 0, bite, 0)
         .Wire(groundIndex, 0, bite, 1)
         .Wire(bite, 0, ground, 2)
         .Wire(subNote, Hz, sub, 1);

        // Nearly always on. The floor is high and the breath on top of it is
        // shallow, because a root that comes and goes is a part rather than a
        // ground, and this is the ground. The one filter on a voice is here, on
        // the one voice with harmonics worth taking off, and its cutoff is the
        // slowest voltage, so the bottom of the mix opens and closes over minutes.
        var groundHold = Span(rootSteps, 0f, 1f, 0.55f, 1f, Gate);
        var groundBreath = b.Add("osc.sine", (1, 0.0133f), (3, 0.25f), (4, 0.75f));
        var groundVoiced = Product(Sum(ground, sub), Product(groundHold, groundBreath));
        var opening = Span(tide, 0f, 1f, 240f, 1500f);
        var shaped = b.Add(FilterType, (2, 0.25f));

        b.Wire(groundVoiced, 0, shaped, 0)
         .Wire(opening, 0, shaped, 1);

        Box("Ground");

        // --- bell ------------------------------------------------------------

        // A sine with one partial in its phase, at just over three times the
        // pitch so the two never quite lock, and the swell is the index as well
        // as the level: the note brightens as it comes up and dulls as it goes,
        // which is what a struck thing does backwards. Through a Phaser slow
        // enough to take most of a minute a turn, so no two swells of the same
        // note have the same shape, and panned by two sines at unrelated rates,
        // which makes it wander across the field rather than swing across it.
        var bellNote = Through("audio.note", bellSteps);
        var bellIndex = Span(flutter, 0f, 1f, 0.08f, 0.22f);
        var bell = b.Add("flyback.voice.bell", (3, 3.01f));

        b.Wire(bellNote, Hz, bell, 1)
         .Wire(bellSteps, Gate, bell, 2)
         .Wire(bellIndex, 0, bell, BellIndex);

        var bellSoft = Times(bell, 0.6f);
        var sweep = b.Add(PhaserType, (1, 0.023f), (2, 0.85f), (3, 0.55f), (4, 0.7f));
        var bellDriftL = b.Add("osc.sine", (1, 0.0311f), (3, 0.4f), (4, 0.55f));
        var bellDriftR = b.Add("osc.sine", (1, 0.0419f), (2, 0.5f), (3, 0.4f), (4, 0.55f));

        b.Wire(bellSoft, 0, sweep, 0);

        var bellL = Product(sweep, bellDriftL);
        var bellR = Product(sweep, bellDriftR);

        Box("Bell");

        // --- the bell leans on the pad ---------------------------------------

        // The second loop, and the first of two followers. The bell's level is
        // read off its own wave, and what it reads turns the pad down: a swell in
        // the bell pushes the pad away in under a second, and the pad takes four
        // to come back. That is the one thing here that happens *because* of
        // something else, and it is what makes two voices sound like one room.
        var bellHeard = Followed(bellSoft, 0, -0.5f, 0.6f);
        var duck = Ducked(bellHeard, 1.6f, 0.3f);

        Box("Bell Follower");

        // --- pad -------------------------------------------------------------

        // A sine and a triangle a few cents apart, with the cents themselves on
        // a sine slower than either — so the beating between them speeds up and
        // slows down instead of sitting at one rate. The triangle is the only
        // plain waveform with harmonics in the patch, and it is here because its
        // odd ones fall off fast enough to fill the middle of the spectrum
        // without ever reaching the top of it. The Chorus after them is what
        // makes the pair stereo: 'out' and 'wide' are swept in opposite
        // directions, which is a wider and cheaper answer than panning.
        var padNote = Through("audio.note", padSteps);
        var detune = b.Add("osc.sine", (1, 0.0233f), (3, 7f));
        var padTwin = b.Add("audio.note");
        var padLower = b.Add("osc.sine", (3, 0.6f));
        var padUpper = b.Add("osc.triangle", (3, 0.45f));

        // And the octave, quietly: a third sine at twice the pitch, which is the
        // one place the middle of the spectrum gets its share from a voice that
        // is otherwise all fundamental.
        var padOctave = b.Add("osc.sine", (3, 0.45f));

        b.Wire(padNote, NoteNumber, padTwin, 0)
         .Wire(detune, 0, padTwin, 2)
         .Wire(padNote, Hz, padLower, 1)
         .Wire(padTwin, Hz, padUpper, 1)
         .Wire(Times(padNote, 2f), 0, padOctave, 1);

        // The swell, on a floor, so the hump reaches something rather than
        // nothing; then breathed on, then ducked under the bell.
        var padSwell = Span(padSteps, 0f, 1f, 0.45f, 1f, Gate);
        var padBreath = b.Add("osc.sine", (1, 0.0173f), (3, 0.2f), (4, 0.8f));
        var padLevel = Product(Product(padSwell, padBreath), duck);
        var padVoiced = Product(Sum(Sum(padLower, padUpper), padOctave), padLevel);
        var thicken = b.Add(ChorusType, (1, 0.19f), (2, 0.75f), (3, 0.6f));

        b.Wire(padVoiced, 0, thicken, 0);

        Box("Pad");

        // --- visitors --------------------------------------------------------

        // Four voices that are not always here. Each has a Wander of its own that
        // has to climb past a threshold before it is let in, so they arrive and
        // leave on no schedule, sometimes together and often none for a while.
        // The gate that lets each one in also marks the picture while it stays.

        // A music box: a plucked string at the top of the scale, on a count of
        // about two a second that skips most of its beats. The note is held from
        // the pluck, so a ringing string is never retuned under itself.
        var boxHere = Presence(0.037f, 6f, 0.56f, 0.68f);
        var boxStroke = Stroke(clock, 2.4f, 4f);
        var boxOdds = Dice(2.4f, 11f);
        var boxSteps = Quantized(Dice(2.4f, 10f, 0.5f, 0.5f), [74f, 77f, 79f, 81f, 84f, 86f, 89f], Held);
        var boxPluck = Formula("step(0.5, a) * step(0.15, b)", boxStroke, new Read(boxOdds, Held));
        var boxHz = b.Add(NodeCatalog.HoldTypeId);
        var box = b.Add(NodeCatalog.StringTypeId, (4, 0.3f));

        b.Wire(Through("audio.note", boxSteps), Hz, boxHz, 0)
         .Wire(boxPluck, 0, boxHz, 1)
         .Wire(boxPluck, 0, box, 1)
         .Wire(boxHz, 0, box, 2);

        Follows(box, 3, chimeDecay, -0.8239087f, 0.69897f); // log10 seconds: 150 ms .. 5 s.

        Box("Visitor: Music Box");

        // A call from far off: a sine gliding between notes of the low half of
        // the scale, in phrases of about five seconds it mostly lets pass, with
        // a vibrato that deepens as the phrase swells.
        var callHere = Presence(0.043f, 7f, 0.56f, 0.68f);
        var phrase = Stroke(clock, 0.21f, 1f);
        var callOdds = Dice(0.21f, 12f);
        var callSwell = Formula(
            "sin(a * pi) * sin(a * pi) * step(-0.3, b)", new Read(phrase, StrokePhase), new Read(callOdds, Held));
        var callSteps = Quantized(Dice(1.3f, 13f, 0.5f, 0.5f), [53f, 55f, 57f, 60f, 62f, 65f, 67f], Held);
        var glide = b.Add(SlewType, (1, -0.45f), (2, -0.45f));
        var vibrato = b.Add("osc.sine", (1, 5.3f));

        b.Wire(Through("audio.note", callSteps), Hz, glide, 0);

        var callHz = Formula("a * (1 + 0.012 * b * c)", glide, vibrato, callSwell);
        var callVoice = Sum(Tone(callHz, callSwell), Tone(Times(callHz, 2.01f), Times(callSwell, 0.2f)));
        var callDriftL = b.Add("osc.sine", (1, 0.0371f), (3, 0.35f), (4, 0.6f));
        var callDriftR = b.Add("osc.sine", (1, 0.0293f), (2, 0.5f), (3, 0.35f), (4, 0.6f));
        var callL = Product(callVoice, callDriftL);
        var callR = Product(callVoice, callDriftR);

        Box("Visitor: Call");

        // Rain: pink hiss high up for the wash, and drops. A drop is a sine that
        // rises in pitch as it dies, on three counts that never line up, each
        // skipping about half its beats and each drop at a pitch of its own.
        var rainHere = Presence(0.031f, 8f, 0.55f, 0.67f);

        NodeInstance Drop(float rate, float seed)
        {
            var fall = Stroke(clock, rate, 7f);
            var dice = Dice(rate, seed);
            var hz = Formula("(700 + 800 * fract(a * 7.31)) * (1.6 - 0.6 * b)", new Read(dice, Held), fall);
            return Tone(hz, Formula("a * step(0, b)", fall, new Read(dice, Held)));
        }

        var dropL = Drop(5.3f, 14f);
        var dropR = Drop(7.7f, 15f);
        var dropMid = Drop(3.1f, 16f);
        var rainWash = Hiss(null, 5200f, 0.15f, "high", 0.35f, "pink", 9f);
        var rainL = Formula("a + b * 0.5 + c", dropL, dropMid, rainWash);
        var rainR = Formula("a + b * 0.5 + c", dropR, dropMid, rainWash);

        Box("Visitor: Rain");

        // Thunder, far off: a slot every five seconds or so that it takes about
        // half the time, heard as low pink noise swelling a beat after the flash
        // and rolling away over the rest of the slot, its cutoff falling with it.
        var stormHere = Presence(0.023f, 9f, 0.58f, 0.7f);
        var strike = Stroke(clock, 0.19f, 1f);
        var strikeOdds = Dice(0.19f, 3f);
        var roll = Formula(
            "(1 - a) * (1 - a) * (1 - a) * smoothstep(0.02, 0.12, a) * step(0, b)",
            new Read(strike, StrokePhase), new Read(strikeOdds, Held));
        var grumble = Wander(1.7f, 10f, 0.45f, 1f);
        var rumble = Hiss(Product(roll, grumble), 120f, 0.35f, "low", 4f, "pink", 11f);

        b.Wire(Span(roll, 0f, 1f, 70f, 260f), 0, rumble, HissCutoff);

        Box("Visitor: Thunder");

        // --- the melody desk -------------------------------------------------

        // The voices that come and go, on a desk of their own, because what the
        // wind below listens to is the sum of exactly these: the ground is
        // nearly always on and would drown the reading.
        var melody = b.Add(DeskType, (DeskTrim, 0f));

        Channel(melody, 1, 0f, thicken, thicken, 0, ChorusWide);
        Channel(melody, 2, 0f, bellL, bellR);
        Channel(melody, 3, 0f, box);
        Channel(melody, 4, 0f, callL, callR);

        b.Wire(Times(boxHere, 0.45f, FadeGate), 0, melody, LevelOf(3))
         .Wire(Times(callHere, 0.5f, FadeGate), 0, melody, LevelOf(4));

        Box("Melody Desk");

        // --- wind ------------------------------------------------------------

        // A Hiss set to pink, on a band whose center is a voltage, which is wind;
        // the resonance is what makes it whistle in the distance rather than
        // hiss. Its level is the third loop: a follower on the melody desk's bus,
        // turned upside down, so the wind comes up when the pad and the bell are
        // quiet and goes when they return. Nothing here decides when that is.
        var melodyHeard = Followed(melody, BusLeft, -0.3f, 0.7f);
        var hush = Ducked(melodyHeard, 2.2f, 0.1f);
        var windDrift = Wander(0.029f, 5f, 0.25f);
        var windVoiced = Hiss(Product(hush, windDrift), 800f, 0.6f, "band", noise: "pink", seed: 4f);

        b.Wire(Span(flutter, 0f, 1f, 500f, 3000f), 0, windVoiced, HissCutoff);
        var windDriftL = b.Add("osc.sine", (1, 0.0533f), (3, 0.45f), (4, 0.5f));
        var windDriftR = b.Add("osc.sine", (1, 0.0631f), (2, 0.5f), (3, 0.45f), (4, 0.5f));
        var windL = Product(windVoiced, windDriftL);
        var windR = Product(windVoiced, windDriftR);

        Box("Wind");

        // --- the desk --------------------------------------------------------

        // The ground and the wind, with the melody desk's bus arriving at full,
        // so the buses out of here carry all four voices. They are read as buses
        // rather than as outputs: what goes into the echo is the sum, and the
        // rails belong after the rooms.
        var desk = b.Add(DeskType);

        Channel(desk, 1, 0.34f, shaped);
        Channel(desk, 2, 0.36f, windL, windR);
        Channel(desk, 3, 0f, rainL, rainR);
        Channel(desk, 4, 0f, rumble);
        Chained(melody, desk);

        b.Wire(Times(rainHere, 0.3f, FadeGate), 0, desk, LevelOf(3))
         .Wire(Times(stormHere, 0.9f, FadeGate), 0, desk, LevelOf(4));

        Box("Desk");

        // --- echo ------------------------------------------------------------

        // The fourth loop, and the one the plugin's Delay does not do on its own:
        // its 'feedback' is left at nothing, and the repeats go round the graph
        // instead — through a lowpass, so each is darker than the last, and a
        // Chorus, so each is a little wider and a little less in tune. A dozen
        // times round, that is a tail that has forgotten what note it was. The
        // gain in the loop is the Multiply, and it is well under one; the filter
        // does not peak at this resonance and the Chorus never gains, so nothing
        // in the ring can grow. The time is a voltage, and a swept delay line
        // glides rather than steps, so what that does to the repeats is tape wow.
        var send = Times(Wired("math.add", desk, desk, BusLeft, BusRight), echoLevel, 0f, 1.3f);
        var ring = b.Add("math.add");
        var echoTime = Span(wander, 0f, -2.3199975f, 0.52f, 0.86f);
        var repeats = b.Add(DelayType, (2, 0f), (3, 1f));
        var darkenTo = Span(tide, 0f, 1f, 800f, 3200f);
        var darken = b.Add(FilterType, (2, 0.1f));
        var smear = b.Add(ChorusType, (1, 0.11f), (2, 0.5f), (3, 0.35f));
        var back = Times(smear, 0.62f);

        b.Wire(send, 0, ring, 0)
         .Wire(back, 0, ring, 1)
         .Wire(ring, 0, repeats, 0)
         .Wire(echoTime, 0, repeats, 1)
         .Wire(repeats, 0, darken, 0)
         .Wire(darkenTo, 0, darken, 1)
         .Wire(darken, FilterLow, smear, 0);

        // The two sides of the Chorus are the two sides of the echo, so the
        // repeats are not in the same place twice.
        var withEchoL = Wired("math.add", desk, Times(smear, 0.8f), BusLeft);
        var withEchoR = Wired("math.add", desk, Times(smear, 0.8f, ChorusWide), BusRight);

        Box("Echo");

        // --- the rooms -------------------------------------------------------

        // Two of them because one would put both sides in the same place, and
        // the sizes are offset so the tails are not the same tail — a reverb is
        // a bank of delays, and two banks a little apart is what a room sounds
        // like from a seat in it rather than from a point in the middle.
        var roomSize = Span(tide, 0f, 1f, 0.72f, 0.98f);
        var roomWide = Span(wander, 0f, 1f, 0.66f, 0.92f);
        var hallL = b.Add(ReverbType, (2, 0.86f));
        var hallR = b.Add(ReverbType, (2, 0.86f));

        b.Wire(withEchoL, 0, hallL, 0)
         .Wire(roomSize, 0, hallL, 1)
         .Wire(withEchoR, 0, hallR, 0)
         .Wire(roomWide, 0, hallR, 1);

        Follows(hallL, 3, reverbSpace, 0f, 0.85f);
        Follows(hallR, 3, reverbSpace, 0f, 0.85f);

        // No drive in front of the master, unlike every other patch with a
        // limiter in it. Ambient has no transients to catch and nothing to gain
        // by being pushed into a wall; the master is here for its rails, because
        // four voices that each breathe on their own will occasionally breathe
        // in at once, and the trim is where the mix is set so that they can.
        var master = b.Add(DeskType, (DeskTrim, 0.7f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.85f));

        Channel(master, 1, 1f, hallL, hallR, 0, ReverbWide);

        b.Wire(master, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(master, 1, output, NodeCatalog.OutputRightPort);

        Box("Rooms & Master", output);

        // --- the picture: geometry -------------------------------------------

        // The only thing on the screen that moves at a steady rate is a rotation,
        // which has nowhere to arrive. Everything else here is one of the
        // voltages, so nothing in the frame is on its way anywhere in particular.
        var creep = Times(clock, 0.011f);
        var placed = TurnedThenZoomed();
        var fold = b.Add("space.kaleidoscope");

        b.Wire(Sum(creep, Span(tide, 0f, 1f, -0.6f, 0.6f)), 0, placed, TransformAngle)
         .Wire(Formula("a * (1 - 0.3 * b * c)", Span(wander, 0f, 1f, 0.75f, 1.45f), callSwell, new Read(callHere, FadeGate)), 0, placed, TransformZoom)
         .Wire(placed, 0, fold, 0)
         .Wire(placed, 1, fold, 1)
         .Wire(Span(tide, 0f, 1f, 2f, 9f), 0, fold, 2);

        // The cloud is Clouds read the ordinary way — per pixel, off the folded
        // plane, boiling on its own clock. The same module as the voltages, and
        // the difference between a source and a texture is entirely in what its
        // x and y are patched to.
        var cloud = b.Add("pattern.clouds", (3, 1.6f));

        b.Wire(fold, 0, cloud, 0)
         .Wire(fold, 1, cloud, 1)
         .Wire(Times(clock, 0.035f), 0, cloud, 2);

        Box("Picture: Geometry");

        // --- the picture: memory ---------------------------------------------

        // The fifth loop, and the one on the screen. A Blend of what this pixel
        // held a frame ago and what it sees now, which is a long exposure with
        // no buffer; how much of the new frame it takes is the fastest voltage,
        // so the picture is sharp for a while and smeared for a while. And what
        // it held is patched back into the geometry: the warp that bends the
        // rings is pushed by the cloud *and* by the memory, so a place that was
        // bright reads the rings from somewhere else than a place that was dark,
        // and the pattern drifts on its own account rather than with the clock.
        // The push is kept small. Larger, a pixel can push itself to somewhere
        // brighter and stay, and the frame fills with light that never leaves.
        var memory = b.Add("math.mix");
        var swirl = Times(memory, 1f);
        var bend = b.Add("space.warp");
        var veil = b.Add("pattern.rings");
        var warpAmount = Times(Span(flutter, 0f, 1f, 0.25f, 0.85f), visualWarp, 0f, 2.5f);

        b.Wire(Span(weather, 0f, 1f, 0.04f, 0.22f), 0, swirl, 1)
         .Wire(fold, 0, bend, 0)
         .Wire(fold, 1, bend, 1)
         .Wire(Sum(cloud, swirl), 0, bend, 2)
         .Wire(warpAmount, 0, bend, 3)
         .Wire(bend, 0, veil, 0)
         .Wire(bend, 1, veil, 1)
         .Wire(Sum(Span(padSteps, 0f, 1f, 1.4f, 3.6f, Index), Times(boxHere, 2.5f, FadeGate)), 0, veil, 2)
         .Wire(Times(clock, 0.09f), 0, veil, 3);

        // Wide edges, unlike every other preset that does this. A hard threshold
        // makes filaments and a soft one makes weather, and the difference is
        // where the two numbers are put.
        var haze = Rises(veil, -0.55f, 0.9f);

        b.Wire(memory, 0, memory, 0)
         .Wire(haze, 0, memory, 1)
         .Wire(Span(flutter, 0f, 1f, 0.06f, 0.25f), 0, memory, 2);

        Box("Picture: Memory");

        // --- the picture: color ----------------------------------------------

        // A Vignette with no picture in it, read for its 'shade': the only thing
        // in the patch that knows where the edge of the frame is.
        var falloff = Vignette(null, 0f, 2.2f, 0.3f);
        var glow = Formula(
            "a * (1 - 0.4 * b)", Span(padSteps, 0f, 1f, 0.9f, 1.6f, Gate), new Read(stormHere, FadeGate));
        var visible = b.Add("math.clamp", (1, 0f), (2, 1f));

        b.Wire(Product(Product(memory, glow), falloff, VignetteShade), 0, visible, 0);

        // Hue off the cloud and the slowest voltage together, so the palette
        // moves across the frame and drifts as a whole at the same time, and the
        // creep under both means it never settles even where the two do.
        var hue = Fraction(Sum(
            Sum(Sum(Sum(Times(cloud, 0.55f), Times(tide, 0.4f)), creep), Times(callHere, 0.22f, FadeGate)),
            Knob(colorShift, 0f, 1f)));
        var fresh = b.Add("color.hsv");

        b.Wire(hue, 0, fresh, 0)
         .Wire(Formula("a * (1 - 0.6 * b)", Span(flutter, 0f, 1f, 0.3f, 0.75f), new Read(rainHere, FadeGate)), 0, fresh, 1)
         .Wire(visible, 0, fresh, 2);

        // The memory above forgets in place; this is the drift. A Trails with
        // nothing patched into it, whose 'tail' is the dimmed last frame read a
        // little zoomed and a little turned, blended under the fresh picture: a
        // delay line has no per-pixel past, and a loop has no elsewhere, and this
        // is the module for the one thing neither of them can do.
        var drift = b.Add(TrailsType, (TrailsZoom, 1.008f), (TrailsPersist, 0.92f));
        var combine = b.Add("color.mix");

        b.Wire(drift, Tail, combine, 0)
         .Wire(fresh, 0, combine, 1)
         .Wire(Span(wander, 0f, 1f, 0.12f, 0.35f), 0, combine, 2);

        Follows(drift,  TrailsZoom, trailsSpin, 0.5f, 2f);
        Follows(drift, TrailsAngle, trailsSpin, -0.015f, 0.015f);

        Box("Picture: Color");

        // --- the picture: visitors -------------------------------------------

        // Each visitor bends something above while it stays: the music box
        // tightens the rings, the call turns the hue and breathes the zoom with
        // its phrase, the rain drains the color and the storm darkens it. These
        // are what each one lays on top as light, after the Trails reads the
        // frame, so a glint or a flash drifts away in the tail.

        // Glints where a pluck lands, at a new place every pluck.
        var glintField = b.Add("pattern.clouds", (3, 11f));
        b.Wire(Times(boxOdds, 9f, Held), 0, glintField, 2);
        var glints = Formula(
            "smoothstep(0.7, 0.8, a) * b * step(0.15, c) * d",
            glintField, boxStroke, new Read(boxOdds, Held), new Read(boxHere, FadeGate));

        // Rain as streaks: noise stretched along a slant and scrolled down it.
        var place = b.Add(NodeCatalog.CoordTypeId);
        var streakField = b.Add("pattern.clouds", (3, 1f));
        b.Wire(Formula("(a + b * 0.18) * 48", place, new Read(place, 1)), 0, streakField, 0)
         .Wire(Formula("a * 1.5 + b * 2.8", new Read(place, 1), clock), 0, streakField, 1);
        var streaks = Formula("smoothstep(0.64, 0.78, a) * b * 0.45", streakField, new Read(rainHere, FadeGate));

        // Lightning: a flicker at the head of each strike, a beat ahead of its
        // thunder, lighting the cloud rather than the whole frame.
        var flash = Formula(
            "(1 - smoothstep(0, 0.05, a)) * step(0, b) * (0.6 + 0.4 * sin(a * 420)) * c * (0.3 + d)",
            new Read(strike, StrokePhase), new Read(strikeOdds, Held), new Read(stormHere, FadeGate), cloud);

        var lit = Ink(combine, glints, 1f, 0.86f, 0.55f);
        var wet = Ink(lit, streaks, 0.6f, 0.74f, 1f);
        var struck = Ink(wet, flash, 0.88f, 0.9f, 1f);

        b.Wire(struck, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Visitors");

        return b.Build();
    }
}
