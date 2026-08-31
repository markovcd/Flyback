using Flyback.Core.Graph;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A whole acid techno track: one sequencer through a resonant filter with an
/// envelope on its cutoff, a four-on-the-floor kick, hats and a clap, into a
/// ping-pong delay — and a picture driven by the same three signals that drive
/// the sound.
/// </summary>
/// <remarks>
/// The largest preset in the box, and the one that needs all three module
/// plugins at once: the Filter and the Drive are Voice's, the Fractal and the
/// palette are Picture's, and the two delay lines are this plugin's own. It is
/// here rather than in either of the others because what makes it a track rather
/// than a riff is the delay.
/// <para>
/// Everything is synthesised. There is no clip and no picture file anywhere in
/// it, which is a constraint worth stating because two of the four instruments
/// would ordinarily be samples: the hats and the clap are both made out of the
/// hash every shader can compute, and the difference between them is which
/// filter response they are read through and how long their envelope is.
/// </para>
/// <para>
/// <b>The acid line.</b> A 303 is one oscillator, one lowpass with a great deal
/// of resonance, and an envelope on the cutoff rather than on the volume — and
/// the last of those is the whole instrument. The amplitude envelope here is
/// almost flat; what moves is the corner frequency, dropping from wherever the
/// envelope threw it back down to where the knob is, once per note. Take the
/// wire out of the Filter's 'cutoff' and the same notes come out sounding like a
/// cheap organ, which is the fastest way to hear what the module is for.
/// </para>
/// <para>
/// The cutoff is three things added together, and they are three different kinds
/// of control. The slow sine is the hand on the knob, moving over half a minute
/// and never repeating with the bar. The envelope is the per-note movement. And
/// the accent is the sequencer's own gate: a step's volume comes out on 'gate'
/// as a level rather than as a switch, so a loud step opens the filter further
/// than a quiet one before the envelope even starts. That is what an accent is
/// on the original machine and it costs one Remap here.
/// </para>
/// <para>
/// <b>Stereo from two delay times.</b> Left is a dotted eighth and right is an
/// eighth, both computed from the tempo rather than typed — a Divide against the
/// sixteenth-note rate, so changing the BPM moves the echoes with it and they
/// stay in time. The two are close enough to read as one space and far enough
/// apart that the repeats walk across the head, which is the whole of the width;
/// there is no pan knob anywhere in the patch.
/// </para>
/// <para>
/// <b>The picture.</b> Three signals cross to it and each one is doing something
/// the others cannot. The slow filter sweep drives the palette's 'spread', so the
/// image opens from tints of one colour to a full spectrum exactly as the sound
/// opens from a hum to a scream — the same wire, not two arrangements that happen
/// to agree. The kick's pulse drives the zoom, the brightness and the twist on
/// the feedback, so the frame moves on the beat. And the sequencer's step index
/// sets how many wedges the kaleidoscope has, so the picture rebuilds itself as
/// the pattern comes round.
/// </para>
/// <para>
/// <b>Why it does not loop.</b> Everything in it used to be sixteen steps long,
/// so every part arrived back at its first step together and the bar was the
/// whole piece. Now the four sequences are thirty-two, twelve, thirty-two and
/// seven steps, and the last two of those share no factor with the bar: the hats
/// arrive three quarters of a bar apart so the open hat walks through the beat,
/// and the seven-step track that sets how far the filter opens takes fourteen
/// bars to line up with the line again. The pattern as a whole comes round after
/// forty-two bars, which is about a minute.
/// </para>
/// <para>
/// Under that, two smooth random voltages read out of a Noise field change the
/// resonance, the drive, the length of the echoes and how loud the hats are —
/// none of which is a note, and all of which is what a hand on a mixer would be
/// doing. They are aperiodic against each other and against the bar, so the
/// piece is never in the same state twice even once the sequences repeat. The
/// hat level being one of them is the arrangement: a level is a socket on the
/// Mixer like any other, so a part fading in and out over a minute costs one
/// wire rather than an automation lane.
/// </para>
/// <para>
/// What deliberately does not cross is the filter envelope. An envelope has no
/// memory on the video path and hands over its gate, so a picture driven by one
/// flickers at the note rate rather than showing the shape; the sweep is a pure
/// function of time and is the same signal at both sinks, which is why it is the
/// one chosen to carry the correspondence. See <c>Kick</c>'s old note in the
/// engine's presets for the same problem answered the other way.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// A plugin is loaded into its own context and does not reference another,
    /// so a preset reaching across a boundary names what it wants the way a
    /// saved patch does — see <c>SlowWeatherPreset</c>, which does the same for
    /// the one module it borrows. It is also why the guards above are worth
    /// having: a string that does not resolve is a module that is not there, and
    /// the complaint should name the plugin rather than the id.
    /// </remarks>
    private const string FilterType = "flyback.voice.filter";

    private const string DriveType = "flyback.voice.drive";

    private const string FractalType = "flyback.picture.fractal";

    private const string PaletteType = "flyback.picture.palette";

    private const string PosteriseType = "flyback.picture.posterise";

    /// <summary>Where a Fractal keeps how many octaves it builds, and under what.</summary>
    private const string FractalState = "fractal";

    private const string OctaveField = "octaves";

    /// <summary>
    /// How many octaves the field builds. The count is a choice on the node
    /// rather than a socket, so it is written as state — the same shape the
    /// module's own helper writes, said here because this assembly cannot call
    /// it.
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
        var clock = b.Add("time", 40, 1900);

        // 130 a minute, which is where this music lives. Everything timed in the
        // patch is one of these two numbers and nothing is typed in seconds.
        var tempo = b.Add(NodeCatalog.TempoTypeId, 40, 2180, (0, 130f));
        var sixteenths = b.Add("math.mul", 260, 2180, (1, 4f));

        b.Wire(tempo, 0, sixteenths, 0);

        b.Group("Clock", clock, tempo, sixteenths);

        // --- the acid line -----------------------------------------------------

        // Two bars of sixteenths in A minor pentatonic, and the second is not the
        // first: it sits higher, jumps further and rests in different places. The
        // volumes are the accents — 0.95 is a step that opens the filter, 0.6 one
        // that does not, and nothing at all is a rest that leaves the pitch where
        // it was, so the notes either side of it are one phrase rather than two.
        var line = b.Add("seq.notes", 500, 1700, (2, 0.5f), (3, 0.02f));
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

        var pitch = b.Add("audio.note", 760, 1620);
        var osc = b.Add("osc.saw", 1000, 1620, (3, 0.9f));

        // Almost flat, which is the point: a 303's volume envelope barely moves
        // and everything you hear happening to a note is the filter.
        var level = b.Add(NodeCatalog.AdsrTypeId, 760, 1900,
            (1, -3f), (2, -1.1f), (3, 0.75f), (4, -1.7f));

        // And the one that does the work. Short, and down to almost nothing, so
        // the cutoff falls away under every note.
        var shape = b.Add(NodeCatalog.AdsrTypeId, 760, 2180,
            (1, -3.2f), (2, -0.95f), (3, 0.05f), (4, -1.6f));

        // The hand on the knob: half a minute a cycle, sharing no factor with
        // the bar, so the track never arrives at the same place twice.
        var sweep = b.Add("osc.sine", 500, 2460, (1, 0.045f));

        // A second hand on a second knob, at a rate that shares nothing with the
        // first: nearly a minute against twenty-two seconds, so the two are never
        // in the same relation twice and the line keeps arriving somewhere new.
        var slower = b.Add("osc.sine", 500, 2620, (1, 0.017f));

        // Seven steps against the line's thirty-two, and this is the other half
        // of why the patch does not repeat. What it carries is not a note but how
        // far the filter envelope is allowed to open, so every seven sixteenths
        // the same phrase is played through a different filter. Seven and
        // thirty-two share no factor, so the pairing takes two hundred and
        // twenty-four sixteenths — fourteen bars — to come round.
        //
        // It is wired into the Remap's 'out high' rather than multiplied onto the
        // result, because that socket is what the envelope's full travel means:
        // a step of nothing is a note with no sweep in it at all, and a step of
        // one throws the cutoff to the top of its range.
        var mutate = b.Add("seq.values", 500, 2740, (2, 0.9f), (3, 0.2f));
        StepsExtra.Set(mutate,
        [
            new Step(0.55f), new Step(1f), new Step(0.72f), new Step(0.3f),
            new Step(0.92f), new Step(0.6f), new Step(0.85f),
        ]);

        var reachTop = b.Add("math.remap", 760, 2740, (1, 0f), (2, 1f), (3, 1400f), (4, 4200f));

        // The three parts of the cutoff, in hertz. The envelope is the biggest
        // by a long way and the accent is the smallest, which is the balance
        // that makes an accent read as emphasis rather than as a second voice.
        var depth = b.Add("math.remap", 1240, 2180, (1, 0f), (2, 1f), (3, 0f));
        var accent = b.Add("math.remap", 1000, 1900, (1, 0f), (2, 1f), (3, 200f), (4, 900f));
        var knob = b.Add("math.remap", 760, 2460, (1, -1f), (2, 1f), (3, 180f), (4, 2600f));

        var floor = b.Add("math.add", 1240, 2280);
        var cutoff = b.Add("math.add", 1480, 2180);

        // Resonance high enough to whistle, which is the sound. Only 'low' is
        // taken; the clap below takes 'band' off a filter of its own.
        var filter = b.Add(FilterType, 1720, 1620);
        var vca = b.Add("math.mul", 1960, 1620);
        var drive = b.Add(DriveType, 2200, 1620);

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
        var leftTime = b.Add("math.div", 2200, 1900, (0, LeftSixteenths));
        var rightTime = b.Add("math.div", 2200, 2180, (0, RightSixteenths));

        var echoL = b.Add(DelayModule.TypeId, 2440, 1620, (3, 0.32f));
        var echoR = b.Add(DelayModule.TypeId, 2440, 1980, (3, 0.32f));

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
        var beat = b.Add("osc.pulse", 500, 2740, (3, 0.02f));

        var thump = b.Add(NodeCatalog.AdsrTypeId, 760, 2740,
            (1, -3f), (2, -0.68f), (3, 0f), (4, -1.1f));

        // The pitch envelope, an order of magnitude shorter than the level one:
        // what the ear hears at the top is the beater and after it the shell.
        var fall = b.Add(NodeCatalog.AdsrTypeId, 760, 3020,
            (1, -3.4f), (2, -1.45f), (3, 0f), (4, -1.9f));

        var boom = b.Add("math.remap", 1000, 3020, (1, 0f), (2, 1f), (3, 48f), (4, 220f));
        var body = b.Add("osc.sine", 1240, 3020);
        var kick = b.Add("math.mul", 1480, 2820);

        b.Wire(tempo, 0, beat, 1)
         .Wire(beat, 0, thump, 0)
         .Wire(beat, 0, fall, 0)
         .Wire(fall, 0, boom, 0)
         .Wire(boom, 0, body, 1)
         .Wire(body, 0, kick, 0)
         .Wire(thump, 0, kick, 1);

        b.Group("Kick", beat, thump, fall, boom, body, kick);

        // --- the hiss both drum sounds are made of ------------------------------

        // Nothing in the catalogue makes a noise a point in the plane can hear:
        // Noise and Fractal are fields in x and y, and the audio path stands at
        // one point of it, so either of them read there is a held tone. What
        // makes the hiss instead is the hash every shader writes — a large
        // multiple of the clock, a sine of it, a larger multiple of that, and
        // the fraction, which lands somewhere else entirely from one sample to
        // the next. Built once here and read by the hats and the clap, because
        // two drums made of the same air is what a drum machine is.
        var grain = b.Add("math.mul", 500, 3300, (1, 3571f));
        var hash = b.Add("math.sin", 760, 3300);
        var scatter = b.Add("math.mul", 1000, 3300, (1, 4371.3f));
        var white = b.Add("math.fract", 1240, 3300);
        var hiss = b.Add("math.remap", 1480, 3300, (1, 0f), (2, 1f), (3, -1f), (4, 1f));

        b.Wire(clock, 0, grain, 0)
         .Wire(grain, 0, hash, 0)
         .Wire(hash, 0, scatter, 0)
         .Wire(scatter, 0, white, 0)
         .Wire(white, 0, hiss, 0);

        b.Group("Hiss", grain, hash, scatter, white, hiss);

        // --- the hats ----------------------------------------------------------

        // The step's own value is a decay time rather than a pitch, which is the
        // one gesture that needs a Sequencer rather than a Note Sequencer: a high
        // step rings and a low one ticks, so the open hats and the closed ones
        // are one instrument and one list.
        //
        // Twelve steps rather than sixteen, which is the whole reason this patch
        // does not sound like a loop. Twelve sixteenths is three quarters of a
        // bar, so the pattern arrives a beat earlier each time round and does not
        // land the same way against the kick until three bars have gone by. The
        // open hat moves through the bar rather than sitting on the same
        // sixteenth for ever.
        var hatSeq = b.Add("seq.values", 500, 3580, (2, 0.3f), (3, 0.01f));
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
        var open = b.Add("math.remap", 760, 3580, (1, 0f), (2, 1f), (3, -2.6f), (4, -1f));
        var hatEnv = b.Add(NodeCatalog.AdsrTypeId, 1000, 3580, (1, -3.8f), (3, 0f), (4, -2.4f));
        var hats = b.Add("math.mul", 1720, 3440);

        b.Wire(sixteenths, 0, hatSeq, 1)
         .Wire(hatSeq, 0, open, 0)
         .Wire(open, 0, hatEnv, 2)
         .Wire(hatSeq, 1, hatEnv, 0)
         .Wire(hiss, 0, hats, 0)
         .Wire(hatEnv, 0, hats, 1);

        b.Group("Hats", hatSeq, open, hatEnv, hats);

        // --- the clap ----------------------------------------------------------

        // Two and four, and the same hiss read through a second Filter — its
        // 'band' this time, which is the output the acid line has no use for.
        // A band of noise around 1.4 kHz with a long-ish tail is a clap; the
        // same noise flat is a hat. One module apart.
        //
        // Two bars, so the answering ghosts differ between them: the backbeat is
        // the thing a listener sets their watch by and never moves, and
        // everything around it does.
        var clapSeq = b.Add("seq.values", 500, 3860, (2, 0.35f), (3, 0.01f));
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

        var crack = b.Add(FilterType, 1720, 3860, (1, 1400f), (2, 0.55f));
        var clapEnv = b.Add(NodeCatalog.AdsrTypeId, 760, 3860,
            (1, -2.7f), (2, -1.15f), (3, 0f), (4, -1.2f));

        var clap = b.Add("math.mul", 1960, 3860);

        b.Wire(sixteenths, 0, clapSeq, 1)
         .Wire(clapSeq, 1, clapEnv, 0)
         .Wire(hiss, 0, crack, 0)
         .Wire(crack, 1, clap, 0)
         .Wire(clapEnv, 0, clap, 1);

        b.Group("Clap", clapSeq, crack, clapEnv, clap);

        // --- the slow weather ----------------------------------------------------

        // Two smooth random voltages, which are what keeps the patch changing
        // once the sequencers have been heard. A Noise field walked slowly along
        // z alone is exactly the lagged sample-and-hold a modular patch would
        // reach for: it wanders rather than stepping, it never repeats, and it
        // costs one module each.
        //
        // Their x and y are pinned by a Value rather than left to the normal,
        // which matters more than it looks. Unpinned they would read the pixel's
        // own position, so the screen would get a field where the speakers get a
        // number, and the two sinks would disagree about what the weather is
        // doing. Held still, both get the same wander. Two far-apart lanes are
        // two unrelated voltages out of one kind of module.
        var lane = b.Add("value", 260, 4150, (0, 0.29f));
        var farLane = b.Add("value", 260, 4450, (0, 2.31f));

        var driftA = b.Add("math.mul", 260, 4300, (1, 0.09f));
        var driftB = b.Add("math.mul", 260, 4600, (1, 0.06f));

        var moodA = b.Add("pattern.noise", 500, 4200, (3, 1f));
        var moodB = b.Add("pattern.noise", 500, 4500, (3, 1f));

        // What A does: the filter's resonance and the drive that follows it,
        // together, because dirt and ring are one thing to the ear and a patch
        // that moved them apart would sound like two faults rather than one
        // hand. Never down to nothing — a 303 with no resonance is not quiet,
        // it is a different instrument.
        var ring = b.Add("math.remap", 760, 2620, (1, -1f), (2, 1f), (3, 0.55f), (4, 0.95f));
        var grit = b.Add("math.remap", 760, 4200, (1, 0f), (2, 1f), (3, 2.2f), (4, 6.5f));

        // And what B does: how long the echoes hang about, and how loud the hats
        // are. The second of those is the arrangement — a level is a socket on
        // the Mixer like any other, so a slow voltage on it is a part fading in
        // and out over a minute or two without anybody writing an automation
        // lane.
        var hang = b.Add("math.remap", 760, 4500, (1, 0f), (2, 1f), (3, 0.24f), (4, 0.6f));

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
        // doing a different job: at this rate a Sequencer is not playing a part,
        // it is deciding how much of the kit is in. Sixteen steps is sixteen
        // bars, about half a minute, and the shape of the list is the shape of
        // the track — two bars of almost nothing, a build, four bars with
        // everything in, a drop back to the floor, and a longer climb.
        //
        // The kick is deliberately not on it. Something has to be the thing the
        // room is counting, and a four-on-the-floor that came and went would take
        // the ground out from under the other two rather than arranging them.
        var bars = b.Add("math.mul", 260, 4780, (1, 0.25f));

        var arrange = b.Add("seq.values", 500, 4780, (2, 0.95f), (3, 0.3f));
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
        var shimmer = b.Add("math.remap", 760, 4700, (1, 0f), (2, 1f), (3, 0.05f), (4, 0.55f));
        var smack = b.Add("math.remap", 760, 4880, (1, 0f), (2, 1f), (3, 0.1f), (4, 0.62f));

        // Half again on the right for the hats and the reverse for the clap, so
        // the two lean opposite ways and keep the width they had when both were
        // knobs.
        var shimmerWide = b.Add("math.mul", 1000, 4700, (1, 1.45f));
        var smackWide = b.Add("math.mul", 1000, 4880, (1, 0.62f));

        b.Wire(tempo, 0, bars, 0)
         .Wire(bars, 0, arrange, 1)
         .Wire(arrange, 0, shimmer, 0)
         .Wire(arrange, 0, smack, 0)
         .Wire(shimmer, 0, shimmerWide, 0)
         .Wire(smack, 0, smackWide, 0);

        b.Group("Arrangement", bars, arrange, shimmer, smack, shimmerWide, smackWide);

        // --- the desk ----------------------------------------------------------

        // Left and right differ in which echo they carry and in how the two drum
        // sounds lean, and in nothing else. There is no pan module in the
        // catalogue and this patch does not want one: width here is two signals
        // that are genuinely different, not one made quieter on a side.
        var deskL = b.Add("math.mixer", 2700, 2400, (1, 0.62f), (3, 1f));
        var deskR = b.Add("math.mixer", 2700, 2900, (1, 0.62f), (3, 1f));

        // Past unity on purpose, with the Clamp after it as the thing that makes
        // that safe: a desk sums the way a desk sums, and four instruments at
        // once is four times over.
        var hotL = b.Add("math.mul", 2940, 2400, (1, 1.15f));
        var hotR = b.Add("math.mul", 2940, 2900, (1, 1.15f));

        var limitL = b.Add("math.clamp", 3180, 2400, (1, -1f), (2, 1f));
        var limitR = b.Add("math.clamp", 3180, 2900, (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, 3900, 1600, (NodeCatalog.OutputGainPort, 0.6f));

        b.Wire(echoL, 0, deskL, 0)
         .Wire(kick, 0, deskL, 2)
         .Wire(hats, 0, deskL, 4)
         .Wire(clap, 0, deskL, 6)

         .Wire(echoR, 0, deskR, 0)
         .Wire(kick, 0, deskR, 2)
         .Wire(hats, 0, deskR, 4)
         .Wire(clap, 0, deskR, 6)

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

        b.Group("Desk", deskL, deskR, hotL, hotR, limitL, limitR);

        // --- the picture: geometry ---------------------------------------------

        // One clock read at three speeds. Multiplies rather than three Times,
        // for the reason Nebula gives: seconds are seconds, and what differs
        // between these is only how much of them each part wants.
        var spin = b.Add("math.mul", 260, 200, (1, 0.04f));
        var boil = b.Add("math.mul", 260, 500, (1, 0.22f));
        var crawl = b.Add("math.mul", 260, 800, (1, 0.015f));

        // The kick moves the light, and it is read as the Pulse rather than
        // through either of its envelopes: an envelope has no memory drawn and
        // would hand over this same gate anyway. It is doing three things — the
        // zoom, the brightness, and the twist on the feedback.
        var pump = b.Add("math.remap", 500, 380, (1, -1f), (2, 1f), (3, 0.96f), (4, 1.3f));

        // And the sequencer moves the frame: where the line has got to in its
        // bar is how many wedges the fold has, so the picture rebuilds itself
        // once a bar rather than once a note.
        var wedges = b.Add("math.remap", 760, 200, (1, 0f), (2, 1f), (3, 3f), (4, 9f));

        // x and y take no wire anywhere in this chain: each is normalled to
        // Coordinates, so it reads the pixel's own position (ADR-0050).
        var turn = b.Add("space.rotate", 500, 80);
        var zoom = b.Add("space.scale", 760, 440);
        var fold = b.Add("space.kaleidoscope", 1000, 200);

        // Read from the folded plane rather than the flat one, so the field is
        // itself symmetric — warping by anything asymmetric here would quietly
        // undo the fold and leave the picture looking like ordinary noise.
        // Five octaves, because detail is the whole of what is being looked at.
        var field = Octaves(
            b.Add(FractalType, 1240, 500, (3, 2.4f), (4, 0.55f)), 5);

        // The sweep again, and this is the wire the preset is built round: the
        // same signal that opens the filter opens the warp and, below, the
        // palette. Nothing is said twice — it is one module read in three places.
        var reach = b.Add("math.remap", 1240, 800, (1, -1f), (2, 1f), (3, 0.15f), (4, 0.6f));
        var bend = b.Add("space.warp", 1480, 200);

        // The line's gate widens the rings, so a sixteenth arrives as a band
        // rather than only as a change of colour.
        var count = b.Add("math.remap", 1480, 800, (1, 0f), (2, 1f), (3, 2.2f), (4, 5.5f));
        var bands = b.Add("pattern.rings", 1720, 200);

        // Rings are a sine, so most of the frame is dark and only the crests
        // survive as filaments.
        var filament = b.Add("math.smoothstep", 1960, 200, (0, 0.2f), (1, 0.9f));

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

        // --- the picture: colour -------------------------------------------------

        // A Palette rather than an HSV hue, which is the difference between a
        // handful of colours that go together and every colour there is. Where
        // in it to look is the field plus the slowest of the three clocks,
        // wrapped rather than clamped because a palette is a loop.
        var wash = b.Add("math.mul", 1480, 1100, (1, 0.7f));
        var slide = b.Add("math.add", 1720, 1100);
        var where = b.Add("math.fract", 1960, 1100);

        // And how wide the palette is comes off the filter sweep. This is the
        // correspondence the whole patch is arranged around: at the bottom of
        // the sweep the sound is a hum and the picture is tints of one colour,
        // and at the top the filter is screaming and the screen is a full
        // spectrum. One wire, two sinks, and neither is illustrating the other.
        var spread = b.Add("math.remap", 1720, 1400, (1, -1f), (2, 1f), (3, 0.06f), (4, 0.42f));

        var palette = b.Add(PaletteType, 2200, 1100, (1, 2f), (3, 0.5f), (4, 0.55f));

        // The kick again, as brightness. Past one on purpose, with the Clamp
        // after it: a value past one is not brighter, it is only wrong.
        var glow = b.Add("math.remap", 1960, 1400, (1, -1f), (2, 1f), (3, 0.5f), (4, 1.7f));
        var lit = b.Add("math.mul", 2200, 1400);
        var visible = b.Add("math.clamp", 2440, 1400, (1, 0f), (2, 1f));

        var inked = b.Add("color.gain", 2680, 1100, (2, 0f));

        // Bands rather than a gradient, because techno is a hard-edged music and
        // a smooth gradient is the wrong picture of it. The count comes off the
        // arrangement rather than off the sweep, so the sections are visible as
        // well as audible: the calm bars are four or five flat colours and the
        // full ones resolve into something detailed enough to be busy.
        var levels = b.Add("math.remap", 2440, 800, (1, 0f), (2, 1f), (3, 5f), (4, 26f));
        var flat = b.Add(PosteriseType, 2940, 1100);

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

        b.Group("Picture: Colour", wash, slide, where, spread, palette, glow, lit, visible,
            inked, levels, flat);

        // --- the picture: feedback ------------------------------------------------

        // The last frame, zoomed in a hair and turned by an amount the kick
        // sets, so the trail lurches on the beat rather than drifting evenly.
        var inward = b.Add("space.scale", 1000, 1700, (2, 1.03f));
        var twist = b.Add("math.remap", 1000, 1400, (1, -1f), (2, 1f), (3, 0.01f), (4, 0.045f));
        var swirl = b.Add("space.rotate", 1240, 1700);
        var past = b.Add("feedback", 1480, 1700);
        var trail = b.Add("color.gain", 1720, 1700, (1, 0.88f), (2, 0f));

        // Max rather than a blend, for FeedbackTunnel's reason: a trail brighter
        // than the new frame keeps its brightness, which is what makes a streak
        // read as a streak rather than as a smeared copy.
        var combine = b.Add("math.max", 3400, 1300);

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

        return b.Patch;
    }
}
