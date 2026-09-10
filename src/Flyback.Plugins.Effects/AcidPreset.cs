using Flyback.Core.Graph;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A whole acid techno track: one sequencer through a resonant filter with an
/// envelope on its cutoff, a sub bass under it, a four-on-the-floor kick, hats
/// and a clap, into a ping-pong delay — and a picture driven by the same three
/// signals that drive the sound.
/// </summary>
internal static class AcidPreset
{
    public const string Name = "Acid";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>
    /// How many sixteenths of a beat each delay is. Left is dotted-eighth and
    /// right is a straight eighth, which is the pairing that walks rather than
    /// bouncing evenly.
    /// </summary>
    private const float LeftSixteenths = 3f;

    private const float RightSixteenths = 2f;

    /// <summary>
    /// The modules this borrows, named by id rather than by type.
    /// </summary>
    private const string FilterType = "flyback.voice.filter";

    private const string DriveType = "flyback.voice.drive";

    private const string FractalType = "flyback.picture.fractal";

    private const string PaletteType = "flyback.picture.palette";

    private const string PosteriseType = "flyback.picture.posterise";

    /// <summary>Where a Fractal keeps how many octaves it builds, and under what.</summary>
    private const string FractalState = "fractal";

    private const string OctaveField = "octaves";

    /// <summary>
    /// How many octaves the field builds. A choice on the node rather than a
    /// socket, so it is written as state — the shape the module's own helper
    /// writes, said here because this assembly cannot call it.
    /// </summary>
    private static NodeInstance Octaves(NodeInstance node, int count)
    {
        node.SetState(FractalState, new JsonObject
        {
            [OctaveField] = JsonValue.Create(count.ToString()),
        });

        return node;
    }

    public static Patch Build(ModuleCatalog modules)
    {
        if (!modules.HasProvider(Voice))
            throw new InvalidOperationException(
                $"it needs the Voice plugin ({Voice}), which is not installed.");

        if (!modules.HasProvider(Picture))
            throw new InvalidOperationException(
                $"it needs the Picture plugin ({Picture}), which is not installed.");

        var b = new PatchBuilder(modules);

        // --- the clock ---------------------------------------------------------

        // Here for the things that have to be told to move and are not an 'in':
        // the Fractal's z, three drift rates, and the hash the hats are made of.
        var clock = b.Add("time");

        // 130 a minute, which is where this music lives. Everything timed in the
        // patch is one of these two numbers and nothing is typed in seconds.
        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 130f));
        var sixteenths = b.Add("math.mul", (1, 4f));

        // Half of that, which is what the bass runs on. Taken off the sixteenths
        // rather than off the tempo again, so the two rates are one number apart
        // by construction and cannot be left disagreeing.
        var eighths = b.Add("math.mul", (1, 0.5f));

        b.Wire(tempo, 0, sixteenths, 0)
         .Wire(sixteenths, 0, eighths, 0);

        b.Group("Clock", clock, tempo, sixteenths, eighths);

        // --- the acid line -----------------------------------------------------

        // Two bars of sixteenths in A minor pentatonic, and the second is not the
        // first. The volumes are the accents — 0.95 opens the filter, 0.6 does
        // not, and a rest leaves the pitch where it was.
        var line = b.Add("seq.notes", (2, 0.5f), (3, 0.02f));
        StepsExtra.Set(line,
        [
            new Step(45f, 1f, 0.95f), new Step(45f, 1f, 0.6f),
            new Step(57f, 1f, 0.9f), new Step(45f, 1f, 0.6f),
            new Step(48f, 1f, 0.95f), new Step(45f, 1f, 0f),
            new Step(52f, 1f, 0.85f), new Step(45f, 1f, 0.6f),
            new Step(43f, 1f, 0.95f), new Step(45f, 1f, 0.6f),
            new Step(55f, 1f, 0.9f), new Step(48f, 1f, 0.6f),
            new Step(45f, 1f, 0.95f), new Step(52f, 1f, 0f),
            new Step(45f, 1f, 0.7f), new Step(57f, 1f, 0.9f),

            new Step(45f, 1f, 0.9f), new Step(57f, 1f, 0.6f),
            new Step(45f, 1f, 0.95f), new Step(57f, 1f, 0.6f),
            new Step(48f, 1f, 0.9f), new Step(60f, 1f, 0.85f),
            new Step(48f, 1f, 0f), new Step(45f, 1f, 0.6f),
            new Step(43f, 1f, 0.95f), new Step(55f, 1f, 0.6f),
            new Step(43f, 1f, 0.9f), new Step(50f, 1f, 0.6f),
            new Step(45f, 1f, 0.95f), new Step(45f, 1f, 0f),
            new Step(57f, 1f, 0.85f), new Step(52f, 1f, 0.7f),
        ]);

        var pitch = b.Add("audio.note");
        var osc = b.Add("osc.saw", (3, 0.9f));

        // Almost flat, which is the point: a 303's volume envelope barely moves
        // and everything you hear happening to a note is the filter.
        var level = b.Add(NodeCatalog.AdsrTypeId, (1, -3f), (2, -1.1f), (3, 0.75f), (4, -1.7f));

        // And the one that does the work. Short, and down to almost nothing, so
        // the cutoff falls away under every note.
        var shape = b.Add(NodeCatalog.AdsrTypeId, (1, -3.2f), (2, -0.95f), (3, 0.05f), (4, -1.6f));

        // The hand on the knob: half a minute a cycle, sharing no factor with
        // the bar, so the track never arrives at the same place twice.
        var sweep = b.Add("osc.sine", (1, 0.045f));

        // A second hand on a second knob, at a rate that shares nothing with the
        // first: nearly a minute against twenty-two seconds, so the two are never
        // in the same relation twice and the line keeps arriving somewhere new.
        var slower = b.Add("osc.sine", (1, 0.017f));

        // Seven steps against the line's thirty-two, carrying how far the filter
        // envelope may open: the two share no factor, so the pairing takes
        // fourteen bars to come round.
        //
        // Wired into the Remap's 'out high' rather than multiplied onto the
        // result, because that socket is what the envelope's full travel means.
        var mutate = b.Add("seq.values", (2, 0.9f), (3, 0.2f));
        StepsExtra.Set(mutate,
        [
            new Step(0.55f), new Step(1f), new Step(0.72f), new Step(0.3f),
            new Step(0.92f), new Step(0.6f), new Step(0.85f),
        ]);

        var reachTop = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1400f), (4, 4200f));

        // The three parts of the cutoff, in hertz. The envelope is the biggest
        // by a long way and the accent is the smallest, which is the balance
        // that makes an accent read as emphasis rather than as a second voice.
        var depth = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0f));
        var accent = b.Add("math.remap", (1, 0f), (2, 1f), (3, 200f), (4, 900f));
        var knob = b.Add("math.remap", (1, -1f), (2, 1f), (3, 180f), (4, 2600f));

        var floor = b.Add("math.add");
        var cutoff = b.Add("math.add");

        // Resonance high enough to whistle, which is the sound. Only 'low' is
        // taken; the clap below takes 'band' off a filter of its own.
        var filter = b.Add(FilterType);
        var vca = b.Add("math.mul");
        var drive = b.Add(DriveType);

        b.Wire(sixteenths, 0, line, 1)
         .Wire(line, 0, pitch, 0)
         .Wire(pitch, 0, osc, 1)
         .Wire(line, 1, level, 0)
         .Wire(line, 1, shape, 0)
         .Wire(shape, 0, depth, 0)
         .Wire(sixteenths, 0, mutate, 1)
         .Wire(mutate, 0, reachTop, 0)
         .Wire(reachTop, 0, depth, 4)
         .Wire(line, 1, accent, 0)
         .Wire(sweep, 0, knob, 0)
         .Wire(knob, 0, floor, 0)
         .Wire(accent, 0, floor, 1)
         .Wire(floor, 0, cutoff, 0)
         .Wire(depth, 0, cutoff, 1)
         .Wire(osc, 0, filter, 0)
         .Wire(cutoff, 0, filter, 1)
         .Wire(filter, 0, vca, 0)
         .Wire(level, 0, vca, 1)
         .Wire(vca, 0, drive, 0);

        b.Group("Acid Line", line, pitch, osc, level, shape, sweep, slower, mutate, reachTop,
            depth, accent, knob, floor, cutoff, filter, vca, drive);

        // --- the echoes --------------------------------------------------------

        // Three sixteenths and two, both worked out from the tempo rather than
        // typed: a Divide with the count on its 'a' and the sixteenth-note rate
        // on its 'b' is that many sixteenths in seconds.
        var leftTime = b.Add("math.div", (0, LeftSixteenths));
        var rightTime = b.Add("math.div", (0, RightSixteenths));

        var echoL = b.Add(DelayModule.TypeId, (3, 0.32f));
        var echoR = b.Add(DelayModule.TypeId, (3, 0.32f));

        b.Wire(sixteenths, 0, leftTime, 1)
         .Wire(sixteenths, 0, rightTime, 1)
         .Wire(drive, 0, echoL, 0)
         .Wire(leftTime, 0, echoL, 1)
         .Wire(drive, 0, echoR, 0)
         .Wire(rightTime, 0, echoR, 1);

        b.Group("Echoes", leftTime, rightTime, echoL, echoR);

        // --- the kick ----------------------------------------------------------

        // Four on the floor, and the one instrument with no sequencer: every
        // beat is the same beat, and a list saying so sixteen times is a list
        // saying nothing. Its 'freq' is the tempo itself.
        var beat = b.Add("osc.pulse", (3, 0.02f));

        // A quarter of a second of fall, which is nearer what a kick needs to be
        // felt than to be heard: what moves air is the tail. Half a beat at this
        // tempo, so the four are four rather than a drone.
        var thump = b.Add(NodeCatalog.AdsrTypeId, (1, -3f), (2, -0.6f), (3, 0f), (4, -1.1f));

        // The pitch envelope, an order of magnitude shorter than the level one:
        // what the ear hears at the top is the beater and after it the shell.
        var fall = b.Add(NodeCatalog.AdsrTypeId, (1, -3.4f), (2, -1.45f), (3, 0f), (4, -1.9f));

        // Forty-two hertz at the bottom rather than forty-eight, which is a
        // sixth of an octave and the difference between a kick with a note in it
        // and one with weight in it. Much lower than this and it stops being
        // reproduced at all; much higher and the whole sound is the beater.
        var boom = b.Add("math.remap", (1, 0f), (2, 1f), (3, 42f), (4, 220f));
        var body = b.Add("osc.sine");
        var kick = b.Add("math.mul");

        // Saturation, which cannot make this louder — the Drive normalises as it
        // goes. What it does is give a sine harmonics, and those are the whole of
        // what a small speaker has of a forty-two hertz note.
        var punch = b.Add(DriveType, (1, 2f));

        b.Wire(tempo, 0, beat, 1)
         .Wire(beat, 0, thump, 0)
         .Wire(beat, 0, fall, 0)
         .Wire(fall, 0, boom, 0)
         .Wire(boom, 0, body, 1)
         .Wire(body, 0, kick, 0)
         .Wire(thump, 0, kick, 1)
         .Wire(kick, 0, punch, 0);

        b.Group("Kick", beat, thump, fall, boom, body, kick, punch);

        // --- the bass ----------------------------------------------------------

        // The floor the patch had none of: the acid line's lowest note is an A at
        // fifty-five hertz and the kick is gone a quarter of a second into every
        // beat. Deliberately not a second acid line — one voice moving is the
        // sound of this music.
        //
        // Sixteen eighths, exactly the length of the line above it: the hats and
        // the mutate track walk against the bar, and a floor that walked would
        // not be one. The notes are the line's own roots, written at its octave
        // and dropped one on the Note module so the list says where the harmony
        // is.
        var bassSeq = b.Add("seq.notes", (2, 0.8f), (3, 0.03f));
        StepsExtra.Set(bassSeq,
        [
            new Step(45f, 1f, 1f), new Step(45f, 1f, 0.7f),
            new Step(45f, 1f, 0.85f), new Step(48f, 1f, 0.75f),
            new Step(43f, 1f, 1f), new Step(43f, 1f, 0.7f),
            new Step(45f, 1f, 0.9f), new Step(45f, 1f, 0.7f),

            new Step(45f, 1f, 1f), new Step(45f, 1f, 0.7f),
            new Step(48f, 1f, 0.9f), new Step(48f, 1f, 0.7f),
            new Step(43f, 1f, 1f), new Step(43f, 1f, 0.75f),
            new Step(45f, 1f, 0.9f), new Step(52f, 1f, 0.8f),
        ]);

        var bassPitch = b.Add("audio.note", (1, -1f));

        // A sine, because the job is the fundamental and nothing else. Anything
        // with harmonics of its own down here would be in the same room as the
        // line, and the line is the thing that is supposed to be heard.
        var bassOsc = b.Add("osc.sine");

        // The opposite envelope to the line's: there the filter does everything
        // and the volume barely moves, here the note simply holds for its eighth.
        // Released over a twentieth of a second rather than cut, because at
        // fifty-five hertz a cut lands mid-cycle and clicks.
        var bassEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -2.4f), (2, -1f), (3, 0.9f), (4, -1.3f));

        var bassVca = b.Add("math.mul");

        // The kick's own level envelope, upside down, on the bass's level — a
        // sidechain compressor written as one Remap, so the bass steps out of the
        // way for the beat and comes straight back.
        //
        // Down to a fifth rather than to nothing: at zero there is a hole on every
        // beat and the ear finds it, and the point of a sidechain is that nobody
        // hears it happening.
        var duck = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1f), (4, 0.2f));
        var ducked = b.Add("math.mul");

        // And the same saturation the kick has, harder, for the same reason: the
        // harmonics of a fifty-five hertz sine are what a small speaker actually
        // reproduces of it, and without them this part is felt on one system and
        // simply absent on every other.
        var weight = b.Add(DriveType, (1, 3.2f));

        b.Wire(eighths, 0, bassSeq, 1)
         .Wire(bassSeq, 0, bassPitch, 0)
         .Wire(bassPitch, 0, bassOsc, 1)
         .Wire(bassSeq, 1, bassEnv, 0)
         .Wire(bassOsc, 0, bassVca, 0)
         .Wire(bassEnv, 0, bassVca, 1)
         .Wire(thump, 0, duck, 0)
         .Wire(bassVca, 0, ducked, 0)
         .Wire(duck, 0, ducked, 1)
         .Wire(ducked, 0, weight, 0);

        b.Group("Bass", bassSeq, bassPitch, bassOsc, bassEnv, bassVca, duck, ducked, weight);

        // --- the hiss both drum sounds are made of ------------------------------

        // Nothing in the catalogue makes a noise a point in the plane can hear:
        // Noise and Fractal are fields in x and y, and the audio path stands at one
        // point of it. What makes the hiss is the hash every shader writes — a
        // large multiple of the clock, a sine, a larger multiple, the fraction.
        // Built once and read by the hats and the clap, because two drums made of
        // the same air is what a drum machine is.
        var grain = b.Add("math.mul", (1, 3571f));
        var hash = b.Add("math.sin");
        var scatter = b.Add("math.mul", (1, 4371.3f));
        var white = b.Add("math.fract");
        var hiss = b.Add("math.remap", (1, 0f), (2, 1f), (3, -1f), (4, 1f));

        b.Wire(clock, 0, grain, 0)
         .Wire(grain, 0, hash, 0)
         .Wire(hash, 0, scatter, 0)
         .Wire(scatter, 0, white, 0)
         .Wire(white, 0, hiss, 0);

        b.Group("Hiss", grain, hash, scatter, white, hiss);

        // --- the hats ----------------------------------------------------------

        // The step's own value is a decay time rather than a pitch, which needs a
        // Sequencer rather than a Note Sequencer: a high step rings and a low one
        // ticks, so open and closed hats are one instrument and one list.
        //
        // Twelve steps rather than sixteen is why this does not sound like a loop:
        // three quarters of a bar, so the pattern arrives a beat earlier each time
        // and takes three bars to land the same way against the kick.
        var hatSeq = b.Add("seq.values", (2, 0.3f), (3, 0.01f));
        StepsExtra.Set(hatSeq,
        [
            new Step(0.12f, 1f, 0.45f), new Step(0.12f, 1f, 0.85f),
            new Step(0.12f, 1f, 0.5f), new Step(0.12f, 1f, 0.85f),
            new Step(0.12f, 1f, 0.45f), new Step(0.55f, 1f, 0.9f),
            new Step(0.12f, 1f, 0.5f), new Step(0.12f, 1f, 0.85f),
            new Step(0.12f, 1f, 0.45f), new Step(0.12f, 1f, 0.9f),
            new Step(1f, 1f, 0.7f), new Step(0.2f, 1f, 0.6f),
        ]);

        // The knob is in decades of seconds, so this is three milliseconds at
        // the bottom of the list and a tenth of a second at the top.
        var open = b.Add("math.remap", (1, 0f), (2, 1f), (3, -2.6f), (4, -1f));
        var hatEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -3.8f), (3, 0f), (4, -2.4f));
        var hats = b.Add("math.mul");

        b.Wire(sixteenths, 0, hatSeq, 1)
         .Wire(hatSeq, 0, open, 0)
         .Wire(open, 0, hatEnv, 2)
         .Wire(hatSeq, 1, hatEnv, 0)
         .Wire(hiss, 0, hats, 0)
         .Wire(hatEnv, 0, hats, 1);

        b.Group("Hats", hatSeq, open, hatEnv, hats);

        // --- the clap ----------------------------------------------------------

        // Two and four, and the same hiss through a second Filter — its 'band'
        // this time, which the acid line has no use for. A band of noise around
        // 1.4 kHz with a longish tail is a clap; the same noise flat is a hat.
        //
        // Two bars, so the answering ghosts differ between them: the backbeat is
        // what a listener sets their watch by, and everything around it moves.
        var clapSeq = b.Add("seq.values", (2, 0.35f), (3, 0.01f));
        StepsExtra.Set(clapSeq,
        [
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.9f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.9f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.35f), new Step(0f, 1f, 0f),

            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.3f),
            new Step(0f, 1f, 0.9f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.4f), new Step(0f, 1f, 0f),
            new Step(0f, 1f, 0.9f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0f), new Step(0f, 1f, 0.5f),
        ]);

        var crack = b.Add(FilterType, (1, 1400f), (2, 0.55f));
        var clapEnv = b.Add(NodeCatalog.AdsrTypeId, (1, -2.7f), (2, -1.15f), (3, 0f), (4, -1.2f));

        var clap = b.Add("math.mul");

        // And a gain past unity on the way out, which is not a taste decision: a
        // bandpass keeps only what fits between its skirts, so this noise carries
        // about a seventh of what the hats do and was inaudible under the drums.
        // The 'band' output is simply a quiet socket.
        var loud = b.Add("math.mul", (1, 3.5f));

        b.Wire(sixteenths, 0, clapSeq, 1)
         .Wire(clapSeq, 1, clapEnv, 0)
         .Wire(hiss, 0, crack, 0)
         .Wire(crack, 1, clap, 0)
         .Wire(clapEnv, 0, clap, 1)
         .Wire(clap, 0, loud, 0);

        b.Group("Clap", clapSeq, crack, clapEnv, clap, loud);

        // --- the slow weather ----------------------------------------------------

        // Two smooth random voltages, which keep the patch changing once the
        // sequencers have been heard: a Noise field walked slowly along z alone is
        // the lagged sample-and-hold a modular patch would reach for.
        //
        // Their x and y are pinned by a Value rather than left to the normal.
        // Unpinned they would read the pixel's own position, so the screen would
        // get a field where the speakers get a number, and the two sinks would
        // disagree about what the weather is doing.
        var lane = b.Add("value", (0, 0.29f));
        var farLane = b.Add("value", (0, 2.31f));

        var driftA = b.Add("math.mul", (1, 0.09f));
        var driftB = b.Add("math.mul", (1, 0.06f));

        var moodA = b.Add("pattern.noise", (3, 1f));
        var moodB = b.Add("pattern.noise", (3, 1f));

        // What A does: the filter's resonance and the drive after it together,
        // because dirt and ring are one thing to the ear. Never down to nothing —
        // a 303 with no resonance is not quiet, it is a different instrument.
        var ring = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.55f), (4, 0.95f));
        var grit = b.Add("math.remap", (1, 0f), (2, 1f), (3, 2.2f), (4, 6.5f));

        // And what B does: how long the echoes hang about, and how loud the hats
        // are. The second is the arrangement — a level is a socket like any other,
        // so a slow voltage on it is a part fading in and out over a minute.
        var hang = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.24f), (4, 0.6f));

        b.Wire(clock, 0, driftA, 0)
         .Wire(clock, 0, driftB, 0)
         .Wire(lane, 0, moodA, 0)
         .Wire(lane, 0, moodA, 1)
         .Wire(driftA, 0, moodA, 2)
         .Wire(farLane, 0, moodB, 0)
         .Wire(farLane, 0, moodB, 1)
         .Wire(driftB, 0, moodB, 2)
         .Wire(slower, 0, ring, 0)
         .Wire(moodA, 0, grit, 0)
         .Wire(moodB, 0, hang, 0)

         .Wire(ring, 0, filter, 2)
         .Wire(grit, 0, drive, 1)
         .Wire(hang, 0, echoL, 2)
         .Wire(hang, 0, echoR, 2);

        b.Group("Slow Weather", lane, farLane, driftA, driftB, moodA, moodB, ring, grit, hang);

        // --- the arrangement -----------------------------------------------------

        // One step a bar rather than one a sixteenth, which is the same module
        // deciding how much of the kit is in rather than playing a part. Sixteen
        // bars is about half a minute, and the shape of the list is the shape of
        // the track.
        //
        // The kick is deliberately not on it: something has to be the thing the
        // room is counting.
        var bars = b.Add("math.mul", (1, 0.25f));

        var arrange = b.Add("seq.values", (2, 0.95f), (3, 0.3f));
        StepsExtra.Set(arrange,
        [
            new Step(0.1f), new Step(0.1f), new Step(0.35f), new Step(0.4f),
            new Step(0.7f), new Step(0.8f), new Step(1f), new Step(1f),
            new Step(0.12f), new Step(0.2f), new Step(0.45f), new Step(0.55f),
            new Step(0.8f), new Step(1f), new Step(1f), new Step(0.6f),
        ]);

        // The two parts that come and go, and the levels they travel between. The
        // bottom of each is not silence: a section with the hats gone entirely
        // reads as the patch having stopped rather than as it having got quiet.
        var shimmer = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.05f), (4, 0.55f));
        var smack = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.1f), (4, 0.62f));

        // Half again on the right for the hats and the reverse for the clap, so
        // the two lean opposite ways and keep the width they had when both were
        // knobs.
        var shimmerWide = b.Add("math.mul", (1, 1.45f));
        var smackWide = b.Add("math.mul", (1, 0.62f));

        b.Wire(tempo, 0, bars, 0)
         .Wire(bars, 0, arrange, 1)
         .Wire(arrange, 0, shimmer, 0)
         .Wire(arrange, 0, smack, 0)
         .Wire(shimmer, 0, shimmerWide, 0)
         .Wire(smack, 0, smackWide, 0);

        b.Group("Arrangement", bars, arrange, shimmer, smack, shimmerWide, smackWide);

        // --- the desk ----------------------------------------------------------

        // Left and right differ in which echo they carry and how the drums lean,
        // and in nothing else. There is no pan module and this patch wants none:
        // width here is two signals that are genuinely different.
        //
        // The kick and the bass are summed before the desk, partly for the four
        // channels a Mixer has and partly because they are one instrument tied
        // together by the duck — a balance to set once rather than twice.
        var lowEnd = b.Add("math.mixer", (1, 1f), (3, 0.6f));

        // The line comes in at a third rather than two thirds, which is what makes
        // the clap audible. The two occupy the same band, so the clap could not be
        // brought out from under it without becoming the loudest thing in the
        // patch. A saw through a resonant filter into two delay lines is a
        // continuous sound, and turning it down makes the gaps in the bar audible
        // again — which is where the clap lives.
        var deskL = b.Add("math.mixer", (1, 0.38f), (3, 1f));
        var deskR = b.Add("math.mixer", (1, 0.38f), (3, 1f));

        // Past unity on purpose, with the Clamp after it as the thing that makes
        // that safe: a desk sums the way a desk sums, and four instruments at
        // once is four times over.
        var hotL = b.Add("math.mul", (1, 1.15f));
        var hotR = b.Add("math.mul", (1, 1.15f));

        var limitL = b.Add("math.clamp", (1, -1f), (2, 1f));
        var limitR = b.Add("math.clamp", (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 0.6f));

        b.Wire(punch, 0, lowEnd, 0)
         .Wire(weight, 0, lowEnd, 2)

         .Wire(echoL, 0, deskL, 0)
         .Wire(lowEnd, 0, deskL, 2)
         .Wire(hats, 0, deskL, 4)
         .Wire(loud, 0, deskL, 6)

         .Wire(echoR, 0, deskR, 0)
         .Wire(lowEnd, 0, deskR, 2)
         .Wire(hats, 0, deskR, 4)
         .Wire(loud, 0, deskR, 6)

         .Wire(shimmer, 0, deskL, 5)
         .Wire(shimmerWide, 0, deskR, 5)
         .Wire(smack, 0, deskL, 7)
         .Wire(smackWide, 0, deskR, 7)

         .Wire(deskL, 0, hotL, 0)
         .Wire(deskR, 0, hotR, 0)
         .Wire(hotL, 0, limitL, 0)
         .Wire(hotR, 0, limitR, 0)
         .Wire(limitL, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(limitR, 0, output, NodeCatalog.OutputRightPort);

        b.Group("Desk", lowEnd, deskL, deskR, hotL, hotR, limitL, limitR);

        // --- the picture: geometry ---------------------------------------------

        // One clock read at three speeds. Multiplies rather than three Times,
        // for the reason Nebula gives: seconds are seconds, and what differs
        // between these is only how much of them each part wants.
        var spin = b.Add("math.mul", (1, 0.04f));
        var boil = b.Add("math.mul", (1, 0.22f));
        var crawl = b.Add("math.mul", (1, 0.015f));

        // The kick moves the light, and it is read as the Pulse rather than
        // through either of its envelopes: an envelope has no memory drawn and
        // would hand over this same gate anyway. It is doing three things — the
        // zoom, the brightness, and the twist on the feedback.
        var pump = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.96f), (4, 1.3f));

        // And the sequencer moves the frame: where the line has got to in its
        // bar is how many wedges the fold has, so the picture rebuilds itself
        // once a bar rather than once a note.
        var wedges = b.Add("math.remap", (1, 0f), (2, 1f), (3, 3f), (4, 9f));

        // x and y take no wire anywhere in this chain: each is normalled to
        // Coordinates, so it reads the pixel's own position (ADR-0050).
        var turn = b.Add("space.rotate");
        var zoom = b.Add("space.scale");
        var fold = b.Add("space.kaleidoscope");

        // Read from the folded plane rather than the flat one, so the field is
        // itself symmetric — warping by anything asymmetric here would quietly
        // undo the fold and leave the picture looking like ordinary noise.
        // Five octaves, because detail is the whole of what is being looked at.
        var field = Octaves(
            b.Add(FractalType, (3, 2.4f), (4, 0.55f)), 5);

        // The sweep again, and this is the wire the preset is built round: the
        // same signal that opens the filter opens the warp and, below, the
        // palette. Nothing is said twice — it is one module read in three places.
        var reach = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.15f), (4, 0.6f));
        var bend = b.Add("space.warp");

        // The line's gate widens the rings, so a sixteenth arrives as a band
        // rather than only as a change of color.
        var count = b.Add("math.remap", (1, 0f), (2, 1f), (3, 2.2f), (4, 5.5f));
        var bands = b.Add("pattern.rings");

        // Rings are a sine, so most of the frame is dark and only the crests
        // survive as filaments.
        var filament = b.Add("math.smoothstep", (0, 0.2f), (1, 0.9f));

        b.Wire(clock, 0, spin, 0)
         .Wire(clock, 0, boil, 0)
         .Wire(clock, 0, crawl, 0)

         .Wire(spin, 0, turn, 2)
         .Wire(beat, 0, pump, 0)
         .Wire(turn, 0, zoom, 0)
         .Wire(turn, 1, zoom, 1)
         .Wire(pump, 0, zoom, 2)

         .Wire(line, 2, wedges, 0)
         .Wire(zoom, 0, fold, 0)
         .Wire(zoom, 1, fold, 1)
         .Wire(wedges, 0, fold, 2)

         .Wire(fold, 0, field, 0)
         .Wire(fold, 1, field, 1)
         .Wire(boil, 0, field, 2)

         .Wire(sweep, 0, reach, 0)
         .Wire(fold, 0, bend, 0)
         .Wire(fold, 1, bend, 1)
         .Wire(field, 1, bend, 2)
         .Wire(reach, 0, bend, 3)

         .Wire(line, 1, count, 0)
         .Wire(bend, 0, bands, 0)
         .Wire(bend, 1, bands, 1)
         .Wire(count, 0, bands, 2)
         .Wire(crawl, 0, bands, 3)
         .Wire(bands, 0, filament, 2);

        b.Group("Picture: Geometry", spin, boil, crawl, pump, wedges, turn, zoom, fold, field,
            reach, bend, count, bands, filament);

        // --- the picture: color -------------------------------------------------

        // A Palette rather than an HSV hue, which is the difference between a
        // handful of colors that go together and every color there is. Where
        // in it to look is the field plus the slowest of the three clocks,
        // wrapped rather than clamped because a palette is a loop.
        var wash = b.Add("math.mul", (1, 0.7f));
        var slide = b.Add("math.add");
        var where = b.Add("math.fract");

        // And how wide the palette is comes off the filter sweep, which is the
        // correspondence the whole patch is arranged around: a hum is tints of one
        // color, and a filter screaming is a full spectrum.
        var spread = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.06f), (4, 0.42f));

        var palette = b.Add(PaletteType, (1, 2f), (3, 0.5f), (4, 0.55f));

        // The kick again, as brightness. Past one on purpose, with the Clamp
        // after it: a value past one is not brighter, it is only wrong.
        var glow = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.5f), (4, 1.7f));
        var lit = b.Add("math.mul");
        var visible = b.Add("math.clamp", (1, 0f), (2, 1f));

        var inked = b.Add("color.gain", (2, 0f));

        // Bands rather than a gradient, because techno is a hard-edged music. The
        // count comes off the arrangement rather than the sweep, so the sections
        // are visible as well as audible.
        var levels = b.Add("math.remap", (1, 0f), (2, 1f), (3, 5f), (4, 26f));
        var flat = b.Add(PosteriseType);

        b.Wire(field, 0, wash, 0)
         .Wire(wash, 0, slide, 0)
         .Wire(crawl, 0, slide, 1)
         .Wire(slide, 0, where, 0)
         .Wire(where, 0, palette, 0)
         .Wire(sweep, 0, spread, 0)
         .Wire(spread, 0, palette, 2)

         .Wire(beat, 0, glow, 0)
         .Wire(filament, 0, lit, 0)
         .Wire(glow, 0, lit, 1)
         .Wire(lit, 0, visible, 0)

         .Wire(palette, 0, inked, 0)
         .Wire(visible, 0, inked, 1)

         .Wire(arrange, 0, levels, 0)
         .Wire(inked, 0, flat, 0)
         .Wire(levels, 0, flat, 1);

        b.Group("Picture: Color", wash, slide, where, spread, palette, glow, lit, visible,
            inked, levels, flat);

        // --- the picture: feedback ------------------------------------------------

        // The last frame, zoomed in a hair and turned by an amount the kick
        // sets, so the trail lurches on the beat rather than drifting evenly.
        var inward = b.Add("space.scale", (2, 1.03f));
        var twist = b.Add("math.remap", (1, -1f), (2, 1f), (3, 0.01f), (4, 0.045f));
        var swirl = b.Add("space.rotate");
        var past = b.Add("feedback");
        var trail = b.Add("color.gain", (1, 0.88f), (2, 0f));

        // Max rather than a blend, for FeedbackTunnel's reason: a trail brighter
        // than the new frame keeps its brightness, which is what makes a streak
        // read as a streak rather than as a smeared copy.
        var combine = b.Add("math.max");

        b.Wire(beat, 0, twist, 0)
         .Wire(inward, 0, swirl, 0)
         .Wire(inward, 1, swirl, 1)
         .Wire(twist, 0, swirl, 2)
         .Wire(swirl, 0, past, 0)
         .Wire(swirl, 1, past, 1)
         .Wire(past, 0, trail, 0)

         .Wire(trail, 0, combine, 0)
         .Wire(flat, 0, combine, 1)
         .Wire(combine, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Picture: Feedback", inward, twist, swirl, past, trail, combine);

        return b.Build();
    }
}
