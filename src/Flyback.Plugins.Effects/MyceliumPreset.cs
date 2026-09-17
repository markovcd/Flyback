using Flyback.Core.Graph;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Effects;

/// <summary>
/// A whole psybient track: ninety-six bars in six sections that come round again,
/// arranged by one sequencer, with a picture grown out of the same signals and
/// one voice that is the picture being heard.
/// </summary>
/// <remarks>
/// The largest patch in the box, and sized against the two things that bound one.
/// The sound runs at about two thirds of one core, which is as much as leaves the
/// audio thread room to be late; every choice below that looks like thrift — an
/// envelope made of the clock, a lowpass made of a wire, one arrangement list
/// instead of a lane a part — is what paid for the reverb, the scan and the
/// seven saws. The picture is the other way about: it runs on the GPU, where
/// eighteen noise lookups a pixel is an afternoon's work, and is as dense as it
/// could be made.
/// </remarks>
internal static class MyceliumPreset
{
    public const string Name = "Mycelium";

    /// <summary>The two plugins this reaches into, named so a failure says which.</summary>
    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    /// <summary>The modules this borrows, named by id rather than by type.</summary>
    private const string SupersawType = "flyback.voice.osc";

    private const string EuclidType = "flyback.voice.euclid";

    private const string DecayType = "flyback.voice.decay";

    private const string SlewType = "flyback.voice.slew";

    private const string FilterType = "flyback.voice.filter";

    private const string DriveType = "flyback.voice.drive";

    private const string FoldType = "flyback.voice.fold";

    private const string FractalType = "flyback.picture.fractal";

    private const string CellsType = "flyback.picture.cells";

    private const string PaletteType = "flyback.picture.palette";

    private const string StarType = "flyback.picture.star";

    private const string PolygonType = "flyback.picture.polygon";

    private const string CircleType = "flyback.picture.circle";

    private const string CombineType = "flyback.picture.combine";

    private const string FillType = "flyback.picture.fill";

    private const string LayerType = "flyback.picture.layer";

    private const string GradeType = "flyback.picture.grade";

    /// <summary>
    /// D Phrygian dominant, written from its root: D, E flat, F sharp, G, A, B flat
    /// and C. The flat second against the major third is the whole accent of this
    /// music.
    /// </summary>
    private static readonly int[] Scale = [2, 3, 6, 7, 9, 10, 0];

    /// <summary>
    /// How many octaves a Fractal builds. A choice on the node rather than a
    /// socket, so it is written as state — the shape the module's own helper
    /// writes, said here because this assembly cannot call it.
    /// </summary>
    private static NodeInstance Octaves(NodeInstance node, int count)
    {
        node.SetState("fractal", new JsonObject { ["octaves"] = JsonValue.Create(count.ToString()) });
        return node;
    }

    /// <summary>A Layer's blend mode, written as the state the module reads.</summary>
    private static NodeInstance Mode(NodeInstance layer, string mode)
    {
        layer.SetState("layer", new JsonObject { ["mode"] = mode });
        return layer;
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

        // --- the clock -------------------------------------------------------

        // A hundred a minute, which is where this music walks rather than runs. Every
        // rate in the patch is this number multiplied, so nothing is typed in seconds
        // and no two parts can be left disagreeing about the tempo.
        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 100f));
        var sixteenths = b.Add("math.mul", (1, 4f));
        var eighths = b.Add("math.mul", (1, 2f));
        var bars = b.Add("math.mul", (1, 0.25f));

        // Eight bars: one step of the arrangement, and the unit the track is built in.
        var phrases = b.Add("math.mul", (1, 0.03125f));

        // The sixteenths, counted rather than sequenced: how many have gone by, the
        // fraction through the one that is playing, and that fraction turned over so it
        // starts at one and falls. Cubed, it is a pluck — an envelope that costs four
        // ops, needs no trigger and cannot drift off the grid, because it is the grid.
        // The bass's filter, the arp, the hats and the lead's bite are this one signal.
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var counted = b.Add("math.mul");
        var within = b.Add("math.fract");
        var tick = b.Add("math.sub", (0, 1f));
        var pluck = b.Add("math.pow", (1, 3f));

        b.Wire(beat, 0, sixteenths, 0)
         .Wire(beat, 0, eighths, 0)
         .Wire(beat, 0, bars, 0)
         .Wire(beat, 0, phrases, 0)
         .Wire(clock, 0, counted, 0)
         .Wire(sixteenths, 0, counted, 1)
         .Wire(counted, 0, within, 0)
         .Wire(within, 0, tick, 1)
         .Wire(tick, 0, pluck, 0);

        b.Group("Clock", beat, sixteenths, eighths, bars, phrases, clock, counted, within, tick,
            pluck);

        // --- the arrangement -------------------------------------------------

        // The whole arrangement is this one list: a number for each eight bars saying
        // how much track there is. Intro, intro, build, build, groove, groove,
        // breakdown, breakdown, peak, peak, peak, outro — twelve steps, ninety-six
        // bars, three minutes fifty and round again.
        //
        // No part has a lane of its own. Each decides below how much of this number it
        // needs before it comes in, so the sections cannot disagree about where they
        // start, and the picture reads the same number to know which section it is
        // drawing.
        var song = b.Add("seq.values");
        StepsExtra.Set(song,
        [
            new Step(0.1f), new Step(0.16f), new Step(0.38f), new Step(0.5f),
            new Step(0.64f), new Step(0.7f), new Step(0.22f), new Step(0.28f),
            new Step(0.9f), new Step(1f), new Step(1f), new Step(0.12f),
        ]);

        // The same number for the parts that fade rather than enter. A drum arrives on
        // a downbeat and wants the step; a pad arriving as a step is a click. Audio
        // only — on the screen a Slew is a wire, so the picture cuts on the section,
        // which is what a picture should do. Four seconds up and two down: the knobs
        // are in decades of a second, here and on every other time in the patch.
        var swell = b.Add(SlewType, (1, 0.60206f), (2, 0.30103f));

        // Which phrases end in a riser: the last of every four, read off the
        // sequencer's own index rather than off a second list, since three times the
        // index has a fraction of three quarters exactly there. The last phrase of all
        // is the outro, which falls away rather than building, so it is masked out.
        var thirds = b.Add("math.mul", (1, 3f));
        var lastOfFour = b.Add("math.fract");
        var turning = b.Add("math.step", (0, 0.7f));
        var notTheEnd = b.Add("math.step", (1, 0.9f));
        var turns = b.Add("math.mul");

        // And how far through the phrase, so that the riser is the second half of it:
        // four bars of climb into the groove, and four into the peak.
        var phrasePos = b.Add("math.mul");
        var throughIt = b.Add("math.fract");
        var secondHalf = b.Add("math.smoothstep", (0, 0.5f), (1, 1f));
        var ramp = b.Add("math.mul");

        // Who is in, as thresholds on the one number. Hard edges for what enters on a
        // downbeat — the kick halfway up the build, the snare only once the groove has
        // arrived — and wide ones for what swells.
        var drumsIn = b.Add("math.smoothstep", (0, 0.42f), (1, 0.48f));
        var snareIn = b.Add("math.smoothstep", (0, 0.55f), (1, 0.62f));
        var bassIn = b.Add("math.smoothstep", (0, 0.3f), (1, 0.45f));
        var hatsIn = b.Add("math.smoothstep", (0, 0.18f), (1, 0.6f));
        var leadIn = b.Add("math.smoothstep", (0, 0.75f), (1, 0.9f));
        var arpIn = b.Add("math.smoothstep", (0, 0.12f), (1, 0.5f));

        // The pad runs the other way: all of the intro and the breakdown, under half
        // of the peak. And the voice of the picture is only there when almost nothing
        // else is.
        var padIn = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1f), (4, 0.45f));
        var scanFade = b.Add("math.remap", (1, 0.2f), (2, 0.35f), (3, 1f), (4, 0f));
        var scanIn = b.Add("math.clamp", (1, 0f), (2, 1f));

        // The harmony, a bar at a time: four on D, two on E flat, two on C. All three
        // are in the scale, so the bass and the pad move in parallel underneath and
        // nothing above them has to be transposed to stay in key. 'shift' is the same
        // thing as semitones away from D, which is what the bass line is written in.
        var root = b.Add("seq.notes");
        StepsExtra.Set(root,
        [
            new Step(38f), new Step(38f), new Step(38f), new Step(38f),
            new Step(39f), new Step(39f), new Step(36f), new Step(36f),
        ]);
        var shift = b.Add("math.sub", (1, 38f));

        b.Wire(phrases, 0, song, 1)
         .Wire(song, 0, swell, 0)
         .Wire(song, 2, thirds, 0)
         .Wire(thirds, 0, lastOfFour, 0)
         .Wire(lastOfFour, 0, turning, 1)
         .Wire(song, 2, notTheEnd, 0)
         .Wire(turning, 0, turns, 0)
         .Wire(notTheEnd, 0, turns, 1)
         .Wire(clock, 0, phrasePos, 0)
         .Wire(phrases, 0, phrasePos, 1)
         .Wire(phrasePos, 0, throughIt, 0)
         .Wire(throughIt, 0, secondHalf, 2)
         .Wire(secondHalf, 0, ramp, 0)
         .Wire(turns, 0, ramp, 1)
         .Wire(song, 0, drumsIn, 2)
         .Wire(song, 0, snareIn, 2)
         .Wire(song, 0, bassIn, 2)
         .Wire(song, 0, hatsIn, 2)
         .Wire(swell, 0, leadIn, 2)
         .Wire(swell, 0, arpIn, 2)
         .Wire(swell, 0, padIn, 0)
         .Wire(swell, 0, scanFade, 0)
         .Wire(scanFade, 0, scanIn, 0)
         .Wire(bars, 0, root, 1)
         .Wire(root, 0, shift, 0);

        b.Group("Arrangement", song, swell, thirds, lastOfFour, turning, notTheEnd, turns,
            phrasePos, throughIt, secondHalf, ramp, drumsIn, snareIn, bassIn, hatsIn, leadIn, arpIn,
            padIn, scanFade, scanIn, root, shift);

        // --- chance ----------------------------------------------------------

        // White noise out of arithmetic — a large multiple of the clock, a sine, a
        // larger multiple, the fraction. Five ops, where the Random module's white
        // is a lookup into the engine's noise, which is eight hashes and a blend.
        var grain = b.Add("math.mul", (1, 3571f));
        var hash = b.Add("math.sin");
        var scatter = b.Add("math.mul", (1, 4371.3f));
        var white = b.Add("math.fract");
        var hiss = b.Add("math.remap", (1, 0f), (2, 1f), (3, -1f), (4, 1f));

        // Where in the Noise field each voltage below is read. Whole numbers, because
        // value noise at a lattice point is the hash itself — which is what makes
        // 'dice' a die rather than a wobble. Pinned by a Value rather than left to the
        // normal, so the screen and the speakers read the same weather.
        var lane = b.Add("value", (0, 3f));
        var farLane = b.Add("value", (0, 7f));

        // One slow voltage for the things that should never repeat: how busy the hats
        // are, how open the pad is, how far the picture bends.
        var slowly = b.Add("math.mul", (1, 0.05f));
        var weather = b.Add("pattern.noise", (3, 1f));

        // A new number every sixteenth. The step count floored is a whole lattice
        // point on all three axes, so the field is read only where it does not
        // interpolate.
        var thrown = b.Add("math.floor");
        var dice = b.Add("pattern.noise", (3, 1f));

        // And one that wanders a bar at a time, which decides when the arp plays and
        // when it rests.
        var barwise = b.Add("math.mul");
        var drift = b.Add("pattern.noise", (3, 1f));

        b.Wire(clock, 0, grain, 0)
         .Wire(grain, 0, hash, 0)
         .Wire(hash, 0, scatter, 0)
         .Wire(scatter, 0, white, 0)
         .Wire(white, 0, hiss, 0)
         .Wire(clock, 0, slowly, 0)
         .Wire(lane, 0, weather, 0)
         .Wire(lane, 0, weather, 1)
         .Wire(slowly, 0, weather, 2)
         .Wire(counted, 0, thrown, 0)
         .Wire(lane, 0, dice, 0)
         .Wire(farLane, 0, dice, 1)
         .Wire(thrown, 0, dice, 2)
         .Wire(clock, 0, barwise, 0)
         .Wire(bars, 0, barwise, 1)
         .Wire(farLane, 0, drift, 0)
         .Wire(farLane, 0, drift, 1)
         .Wire(barwise, 0, drift, 2);

        b.Group("Chance", grain, hash, scatter, white, hiss, lane, farLane, slowly, weather, thrown,
            dice, barwise, drift);

        // --- the kick --------------------------------------------------------

        // Three in eight over the eighths — the tresillo, which is the dub half of
        // psydub. A Euclid rather than a list because the rhythm is two numbers.
        var kickHits = b.Add(EuclidType, (5, 0.4f));

        // A Decay rather than an ADSR: it falls from the hit whatever the gate does
        // afterwards. Half a millisecond up and four tenths of a second down, which
        // is a kick that is felt.
        var thump = b.Add(DecayType, (1, -3.30103f), (2, -0.39794f), (3, 0.65f));

        // The pitch envelope is the level envelope to the fifth power, so there is one
        // envelope and the beater is over long before the shell is. Forty-one hertz at
        // the bottom of the fall.
        var snap = b.Add("math.pow", (1, 5f));
        var fall = b.Add("math.remap", (1, 0f), (2, 1f), (3, 41f), (4, 191f));
        var kickBody = b.Add("osc.sine");
        var kickVoiced = b.Add("math.mul");

        // Harmonics, which are all a small speaker has of forty-one hertz. The Drive
        // normalizes as it goes, so this cannot make the kick louder.
        var kickPunch = b.Add(DriveType, (1, 2.5f));
        var kickOut = b.Add("math.mul");

        // The sidechain: the kick's own level, upside down, on the bass. Down to a
        // third rather than to nothing, because a hole on every beat is heard and the
        // point of a duck is that nobody hears it.
        var kickLevel = b.Add("math.mul");
        var duck = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1f), (4, 0.3f));

        b.Wire(eighths, 0, kickHits, 1)
         .Wire(kickHits, 0, thump, 0)
         .Wire(thump, 0, snap, 0)
         .Wire(snap, 0, fall, 0)
         .Wire(fall, 0, kickBody, 1)
         .Wire(kickBody, 0, kickVoiced, 0)
         .Wire(thump, 0, kickVoiced, 1)
         .Wire(kickVoiced, 0, kickPunch, 0)
         .Wire(kickPunch, 0, kickOut, 0)
         .Wire(drumsIn, 0, kickOut, 1)
         .Wire(thump, 0, kickLevel, 0)
         .Wire(drumsIn, 0, kickLevel, 1)
         .Wire(kickLevel, 0, duck, 0);

        b.Group("Kick", kickHits, thump, snap, fall, kickBody, kickVoiced, kickPunch, kickOut,
            kickLevel, duck);

        // --- the snare -------------------------------------------------------

        // Two and four with no sequencer and no envelope: half the beat rate, offset
        // by half a cycle, is a ramp that restarts on every backbeat. Turned over and
        // raised to the tenth it is a quarter of a second of fall.
        var halfBeat = b.Add("math.mul", (1, 0.5f));
        var halfCount = b.Add("math.mul");
        var offBeat = b.Add("math.add", (1, 0.5f));
        var sinceSnare = b.Add("math.fract");
        var snareTick = b.Add("math.sub", (0, 1f));
        var backbeat = b.Add("math.pow", (1, 10f));

        // The band of a Filter around two kilohertz is the wires, made loud because a
        // bandpass keeps only what fits between its skirts, and a sine at the shell's
        // pitch is the drum under them.
        var rattle = b.Add(FilterType, (1, 1900f), (2, 0.3f));
        var rattleLoud = b.Add("math.mul", (1, 2.7f));
        var frequency = b.Add("audio.frequency", (0, 185f));
        var shell = b.Add("osc.sine", (3, 0.5f));
        var snareSum = b.Add("math.add");
        var snareHit = b.Add("math.mul");
        var snareOut = b.Add("math.mul");

        b.Wire(beat, 0, halfBeat, 0)
         .Wire(clock, 0, halfCount, 0)
         .Wire(halfBeat, 0, halfCount, 1)
         .Wire(halfCount, 0, offBeat, 0)
         .Wire(offBeat, 0, sinceSnare, 0)
         .Wire(sinceSnare, 0, snareTick, 1)
         .Wire(snareTick, 0, backbeat, 0)
         .Wire(hiss, 0, rattle, 0)
         .Wire(rattle, 1, rattleLoud, 0)
         .Wire(frequency, 0, shell, 1)
         .Wire(rattleLoud, 0, snareSum, 0)
         .Wire(shell, 0, snareSum, 1)
         .Wire(snareSum, 0, snareHit, 0)
         .Wire(backbeat, 0, snareHit, 1)
         .Wire(snareHit, 0, snareOut, 0)
         .Wire(snareIn, 0, snareOut, 1);

        b.Group("Snare", halfBeat, halfCount, offBeat, sinceSnare, snareTick, backbeat, rattle,
            rattleLoud, frequency, shell, snareSum, snareHit, snareOut);

        // --- the hats --------------------------------------------------------

        // How many of sixteen steps carry a hat, off the weather: five is sparse and
        // twelve is a shaker. A Euclid spreads whatever number arrives evenly again,
        // so the pattern changes shape rather than merely filling in.
        var density = b.Add("math.remap", (1, 0.2f), (2, 0.8f), (3, 5f), (4, 12f));
        var hatHits = b.Add(EuclidType, (2, 16f), (4, 2f), (5, 0.3f));

        // The pluck again, steeper, and only on the steps the Euclid says. 'hit' rather
        // than 'gate', because the clock is already the envelope.
        var hatTick = b.Add("math.pow", (1, 7f));
        var hatEnv = b.Add("math.mul");
        var hatVoiced = b.Add("math.mul");
        var hatOut = b.Add("math.mul");

        b.Wire(weather, 0, density, 0)
         .Wire(sixteenths, 0, hatHits, 1)
         .Wire(density, 0, hatHits, 3)
         .Wire(tick, 0, hatTick, 0)
         .Wire(hatTick, 0, hatEnv, 0)
         .Wire(hatHits, 1, hatEnv, 1)
         .Wire(hiss, 0, hatVoiced, 0)
         .Wire(hatEnv, 0, hatVoiced, 1)
         .Wire(hatVoiced, 0, hatOut, 0)
         .Wire(hatsIn, 0, hatOut, 1);

        b.Group("Hats", density, hatHits, hatTick, hatEnv, hatVoiced, hatOut);

        // --- the bass --------------------------------------------------------

        // The rolling line: silent on the beat, where the kick is, then three
        // sixteenths. The rests are the note silenced rather than no note, so the
        // pitch holds through them and the Filter is not swept by a rest. An octave
        // jump closes each half bar.
        var bassSeq = b.Add("seq.notes", (2, 0.7f), (3, 0.03f));
        StepsExtra.Set(bassSeq,
        [
            new Step(38f, 1f, 0f), new Step(38f), new Step(38f, 1f, 0.8f), new Step(38f, 1f, 0.9f),
            new Step(38f, 1f, 0f), new Step(38f), new Step(38f, 1f, 0.8f), new Step(50f, 1f, 0.9f),
            new Step(38f, 1f, 0f), new Step(38f), new Step(38f, 1f, 0.8f), new Step(38f, 1f, 0.9f),
            new Step(38f, 1f, 0f), new Step(38f), new Step(50f, 1f, 0.85f), new Step(38f, 1f, 0.9f),
        ]);
        var bassNote = b.Add("math.add");
        var bassHz = b.Add("audio.note");
        var bassSaw = b.Add("osc.saw", (3, 0.7f));
        var bassSine = b.Add("osc.sine", (3, 0.6f));
        var bassBody = b.Add("math.add");

        // How far the pluck opens the Filter is the arrangement: a thud in the build
        // and a bark at the peak, on the same notes.
        var bassReach = b.Add("math.remap", (1, 0f), (2, 1f), (3, 300f), (4, 2600f));
        var bassSweep = b.Add("math.mul");
        var bassCut = b.Add("math.add", (1, 110f));
        var bassTone = b.Add(FilterType, (2, 0.45f));
        var bassVoiced = b.Add("math.mul");
        var bassGrit = b.Add(DriveType, (1, 3f));

        // The sub, an octave under and added after the Drive so that it stays a sine:
        // dirt down here is mud. It is the part of the patch that is felt rather than
        // heard, and the duck covers it with the rest of the bass.
        var subHz = b.Add("math.mul", (1, 0.5f));
        var subOsc = b.Add("osc.sine", (3, 0.55f));
        var sub = b.Add("math.mul");
        var bassSum = b.Add("math.add");
        var bassDucked = b.Add("math.mul");
        var bassOut = b.Add("math.mul");

        b.Wire(sixteenths, 0, bassSeq, 1)
         .Wire(bassSeq, 0, bassNote, 0)
         .Wire(shift, 0, bassNote, 1)
         .Wire(bassNote, 0, bassHz, 0)
         .Wire(bassHz, 0, bassSaw, 1)
         .Wire(bassHz, 0, bassSine, 1)
         .Wire(bassSaw, 0, bassBody, 0)
         .Wire(bassSine, 0, bassBody, 1)
         .Wire(swell, 0, bassReach, 0)
         .Wire(pluck, 0, bassSweep, 0)
         .Wire(bassReach, 0, bassSweep, 1)
         .Wire(bassSweep, 0, bassCut, 0)
         .Wire(bassBody, 0, bassTone, 0)
         .Wire(bassCut, 0, bassTone, 1)
         .Wire(bassTone, 0, bassVoiced, 0)
         .Wire(bassSeq, 1, bassVoiced, 1)
         .Wire(bassVoiced, 0, bassGrit, 0)
         .Wire(bassHz, 0, subHz, 0)
         .Wire(subHz, 0, subOsc, 1)
         .Wire(subOsc, 0, sub, 0)
         .Wire(bassSeq, 1, sub, 1)
         .Wire(bassGrit, 0, bassSum, 0)
         .Wire(sub, 0, bassSum, 1)
         .Wire(bassSum, 0, bassDucked, 0)
         .Wire(duck, 0, bassDucked, 1)
         .Wire(bassDucked, 0, bassOut, 0)
         .Wire(bassIn, 0, bassOut, 1);

        b.Group("Bass", bassSeq, bassNote, bassHz, bassSaw, bassSine, bassBody, bassReach,
            bassSweep, bassCut, bassTone, bassVoiced, bassGrit, subHz, subOsc, sub, bassSum,
            bassDucked, bassOut);

        // --- the arp ---------------------------------------------------------

        // A tune nobody wrote. A sine one bar long is the contour, the die is thrown
        // on every sixteenth to knock it about by half an octave, and the Quantiser
        // snaps what results to the scale. With no 'hold' it snaps continuously, which
        // is right here: the die only moves on the grid.
        var arc = b.Add("osc.sine", (3, 9f), (4, 66f));
        var jitter = b.Add("math.remap", (1, 0f), (2, 1f), (3, -6f), (4, 6f));
        var contour = b.Add("math.add");
        var arpNote = b.Add(NodeCatalog.QuantiserTypeId);
        ScaleExtra.Set(arpNote, Scale);
        var arpHz = b.Add("audio.note");

        // It rests whenever the drift is low, so it comes and goes in phrases rather
        // than in notes.
        var arpGate = b.Add("math.step", (0, 0.4f));
        var arpWidth = b.Add("osc.sine", (1, 0.13f), (3, 0.3f), (4, 0.5f));
        var arpOsc = b.Add("osc.pulse", (4, 0.8f));
        var arpPlucked = b.Add("math.mul");
        var arpTone = b.Add("math.mul");

        // The Filter after the pluck rather than before it, so the instant rise the
        // clock's envelope has is rounded off on the way out.
        var arpOpen = b.Add("math.remap", (1, 0f), (2, 1f), (3, 600f), (4, 3800f));
        var arpVoice = b.Add(FilterType, (2, 0.45f));
        var arpOut = b.Add("math.mul");

        b.Wire(bars, 0, arc, 1)
         .Wire(dice, 0, jitter, 0)
         .Wire(arc, 0, contour, 0)
         .Wire(jitter, 0, contour, 1)
         .Wire(contour, 0, arpNote, 0)
         .Wire(arpNote, 0, arpHz, 0)
         .Wire(drift, 0, arpGate, 1)
         .Wire(arpHz, 0, arpOsc, 1)
         .Wire(arpWidth, 0, arpOsc, 3)
         .Wire(arpOsc, 0, arpPlucked, 0)
         .Wire(pluck, 0, arpPlucked, 1)
         .Wire(arpPlucked, 0, arpTone, 0)
         .Wire(arpGate, 0, arpTone, 1)
         .Wire(swell, 0, arpOpen, 0)
         .Wire(arpTone, 0, arpVoice, 0)
         .Wire(arpOpen, 0, arpVoice, 1)
         .Wire(arpVoice, 0, arpOut, 0)
         .Wire(arpIn, 0, arpOut, 1);

        b.Group("Arp", arc, jitter, contour, arpNote, arpHz, arpGate, arpWidth, arpOsc, arpPlucked,
            arpTone, arpOpen, arpVoice, arpOut);

        // --- the drips -------------------------------------------------------

        // Five in twelve against everything else's sixteen, so it takes three bars to
        // land the same way twice.
        var dripHits = b.Add(EuclidType, (2, 12f), (3, 5f), (4, 3f), (5, 0.3f));

        // The arp's note an octave up, caught by a Sample & Hold on the hit. A String's
        // pitch is the length of its delay line, and one that moved while it rang
        // would bend. Nine tenths of a second of ring, and dark.
        var dripPitch = b.Add("audio.note", (1, 1f));
        var dripHz = b.Add(NodeCatalog.HoldTypeId);
        var dripString = b.Add(NodeCatalog.StringTypeId, (3, -0.04575749f), (4, 0.35f));
        var dripOut = b.Add("math.mul");

        b.Wire(sixteenths, 0, dripHits, 1)
         .Wire(arpNote, 0, dripPitch, 0)
         .Wire(dripPitch, 0, dripHz, 0)
         .Wire(dripHits, 0, dripHz, 1)
         .Wire(dripHits, 0, dripString, 1)
         .Wire(dripHz, 0, dripString, 2)
         .Wire(dripString, 0, dripOut, 0)
         .Wire(padIn, 0, dripOut, 1);

        b.Group("Drips", dripHits, dripPitch, dripHz, dripString, dripOut);

        // --- the lead --------------------------------------------------------

        // The one melody that was written down, kept back for the peak. The rests
        // hold their pitch, so the glide below slides between the notes either side of
        // a gap rather than up from nothing.
        var leadSeq = b.Add("seq.notes", (2, 0.85f), (3, 0.06f));
        StepsExtra.Set(leadSeq,
        [
            new Step(62f), new Step(62f, 1f, 0f), new Step(63f), new Step(62f),
            new Step(66f), new Step(62f, 1f, 0f), new Step(69f), new Step(67f, 1f, 0.8f),
            new Step(74f), new Step(72f, 1f, 0f), new Step(70f), new Step(69f, 1f, 0.8f),
            new Step(67f), new Step(66f, 1f, 0.8f), new Step(63f), new Step(62f, 1f, 0f),
        ]);

        // Twenty-five milliseconds of glide either way.
        var glide = b.Add(SlewType, (1, -1.60206f), (2, -1.60206f));
        var leadHz = b.Add("audio.note");
        var leadSaw = b.Add("osc.saw", (3, 0.8f));

        // Two hands on two knobs at rates that share nothing: how hard the saw is
        // folded, which is its harmonics, and where the Filter rests, which is how many
        // of them get out.
        var foldHand = b.Add("osc.sine", (1, 0.037f));
        var foldDepth = b.Add("math.remap", (1, -1f), (2, 1f), (3, 1.2f), (4, 3.2f));
        var folded = b.Add(FoldType);

        // The pluck on the notes that sound, which is the squelch: resonance this high
        // turns a falling cutoff into a vowel.
        var biteEnv = b.Add("math.mul");
        var biteReach = b.Add("math.mul", (1, 2600f));
        var cutHand = b.Add("osc.sine", (1, 0.021f));
        var cutFloor = b.Add("math.remap", (1, -1f), (2, 1f), (3, 350f), (4, 1500f));
        var bite = b.Add("math.add");
        var squelch = b.Add(FilterType, (2, 0.78f));
        var leadVoiced = b.Add("math.mul");
        var leadOut = b.Add("math.mul");

        b.Wire(sixteenths, 0, leadSeq, 1)
         .Wire(leadSeq, 0, glide, 0)
         .Wire(glide, 0, leadHz, 0)
         .Wire(leadHz, 0, leadSaw, 1)
         .Wire(foldHand, 0, foldDepth, 0)
         .Wire(leadSaw, 0, folded, 0)
         .Wire(foldDepth, 0, folded, 1)
         .Wire(leadSeq, 1, biteEnv, 0)
         .Wire(pluck, 0, biteEnv, 1)
         .Wire(biteEnv, 0, biteReach, 0)
         .Wire(cutHand, 0, cutFloor, 0)
         .Wire(biteReach, 0, bite, 0)
         .Wire(cutFloor, 0, bite, 1)
         .Wire(folded, 0, squelch, 0)
         .Wire(bite, 0, squelch, 1)
         .Wire(squelch, 0, leadVoiced, 0)
         .Wire(leadSeq, 1, leadVoiced, 1)
         .Wire(leadVoiced, 0, leadOut, 0)
         .Wire(leadIn, 0, leadOut, 1);

        b.Group("Lead", leadSeq, glide, leadHz, leadSaw, foldHand, foldDepth, folded, biteEnv,
            biteReach, cutHand, cutFloor, bite, squelch, leadVoiced, leadOut);

        // --- the pad ---------------------------------------------------------

        // Root, fifth and octave over the harmony: seven detuned saws for the root,
        // and a triangle and a sine above it that add pitch without adding beating.
        // Both are the root's frequency multiplied rather than two more Notes: an
        // equal-tempered fifth is one number, and an octave is two.
        var padNote = b.Add("math.add", (1, 12f));
        var padHz = b.Add("audio.note");
        var strings = b.Add(SupersawType, (2, 0.35f), (3, 0.8f), (5, 0.5f));
        var fifthHz = b.Add("math.mul", (1, 1.498307f));
        var fifth = b.Add("osc.triangle", (3, 0.35f));
        var octaveHz = b.Add("math.mul", (1, 2f));
        var octave = b.Add("osc.sine", (3, 0.25f));
        var padPair = b.Add("math.add");
        var padChord = b.Add("math.add");

        // Opened and closed by the weather, and breathing on a period that shares
        // nothing with the bar.
        var padCut = b.Add("math.remap", (1, 0.2f), (2, 0.8f), (3, 350f), (4, 1700f));
        var padTone = b.Add(FilterType, (2, 0.25f));
        var breath = b.Add("osc.sine", (1, 0.043f), (3, 0.2f), (4, 0.8f));
        var padBreathed = b.Add("math.mul");
        var padVoiced = b.Add("math.mul");

        // The Chorus is what makes it stereo: 'out' and 'wide' are swept in opposite
        // directions, which is wider than panning and costs one module.
        var wide = b.Add(ChorusModule.TypeId, (1, 0.17f), (2, 0.7f), (3, 0.6f));

        b.Wire(root, 0, padNote, 0)
         .Wire(padNote, 0, padHz, 0)
         .Wire(padHz, 0, strings, 1)
         .Wire(padHz, 0, fifthHz, 0)
         .Wire(fifthHz, 0, fifth, 1)
         .Wire(padHz, 0, octaveHz, 0)
         .Wire(octaveHz, 0, octave, 1)
         .Wire(strings, 0, padPair, 0)
         .Wire(fifth, 0, padPair, 1)
         .Wire(padPair, 0, padChord, 0)
         .Wire(octave, 0, padChord, 1)
         .Wire(weather, 0, padCut, 0)
         .Wire(padChord, 0, padTone, 0)
         .Wire(padCut, 0, padTone, 1)
         .Wire(padTone, 0, padBreathed, 0)
         .Wire(breath, 0, padBreathed, 1)
         .Wire(padBreathed, 0, padVoiced, 0)
         .Wire(padIn, 0, padVoiced, 1)
         .Wire(padVoiced, 0, wide, 0);

        b.Group("Pad", padNote, padHz, strings, fifthHz, fifth, octaveHz, octave, padPair, padChord,
            padCut, padTone, breath, padBreathed, padVoiced, wide);

        // --- the picture: space ----------------------------------------------

        // The plane turns slowly all the time and jumps to a new angle with every
        // section.
        var creep = b.Add("math.mul", (1, 0.03f));
        var sectionTurn = b.Add("math.mul", (1, 2f));
        var spin = b.Add("math.add");

        // The kick as the screen sees it: the Euclid's own gate, since a Decay drawn
        // is its trigger. It pushes the whole plane in a little on every hit.
        var kickSeen = b.Add("math.mul");
        var pump = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1f), (4, 0.92f));

        // How many wedges is the section, three in the intro and nine at the peak.
        // Floored: a fold count between two whole numbers is a kaleidoscope with a
        // seam in it.
        var wedgeCount = b.Add("math.remap", (1, 0f), (2, 1f), (3, 3f), (4, 9f));
        var wedges = b.Add("math.floor");
        var turn = b.Add("space.rotate");
        var zoom = b.Add("space.scale");
        var plane = b.Add("space.kaleidoscope");

        b.Wire(clock, 0, creep, 0)
         .Wire(song, 0, sectionTurn, 0)
         .Wire(creep, 0, spin, 0)
         .Wire(sectionTurn, 0, spin, 1)
         .Wire(kickHits, 0, kickSeen, 0)
         .Wire(drumsIn, 0, kickSeen, 1)
         .Wire(kickSeen, 0, pump, 0)
         .Wire(song, 0, wedgeCount, 0)
         .Wire(wedgeCount, 0, wedges, 0)
         .Wire(spin, 0, turn, 2)
         .Wire(turn, 0, zoom, 0)
         .Wire(turn, 1, zoom, 1)
         .Wire(pump, 0, zoom, 2)
         .Wire(zoom, 0, plane, 0)
         .Wire(zoom, 1, plane, 1)
         .Wire(wedges, 0, plane, 2);

        b.Group("Picture: Space", creep, sectionTurn, spin, kickSeen, pump, wedgeCount, wedges,
            turn, zoom, plane);

        // --- the picture: growth ---------------------------------------------

        // Three octaves, because this field is also heard — the Scan reads it at audio
        // rate, and every octave is a noise lookup a sample.
        var boil = b.Add("math.mul", (1, 0.1f));
        var field = Octaves(b.Add(FractalType, (3, 2.2f), (4, 0.55f)), 3);
        var reach = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.1f), (4, 0.5f));
        var bent = b.Add("space.warp");

        // The mycelium: Cells read off the bent plane, more and smaller as the track
        // fills. 'edge' is nothing on the wall between two cells and rises inside
        // them, so one minus a Smoothstep of it is the walls alone, lit.
        var churn = b.Add("math.mul", (1, 0.12f));
        var cellSize = b.Add("math.remap", (1, 0f), (2, 1f), (3, 3f), (4, 6.5f));
        var colony = b.Add(CellsType, (4, 0.9f));
        var wall = b.Add("math.smoothstep", (0, 0f), (1, 0.09f));
        var threads = b.Add("math.sub", (0, 1f));

        // Which cells flash with the hats. Each cell's own number, scattered, plus a
        // step that moves on with the pattern; the top eighth of what results is
        // chosen, and a different eighth on the next hit.
        var cellDice = b.Add("math.mul", (1, 13.7f));
        var hatStep = b.Add("math.mul", (1, 5f));
        var cellPick = b.Add("math.add");
        var cellRoll = b.Add("math.fract");
        var chosen = b.Add("math.step", (0, 0.88f));
        var sparkHit = b.Add("math.mul");
        var spark = b.Add("math.mul");

        b.Wire(clock, 0, boil, 0)
         .Wire(plane, 0, field, 0)
         .Wire(plane, 1, field, 1)
         .Wire(boil, 0, field, 2)
         .Wire(weather, 0, reach, 0)
         .Wire(plane, 0, bent, 0)
         .Wire(plane, 1, bent, 1)
         .Wire(field, 0, bent, 2)
         .Wire(reach, 0, bent, 3)
         .Wire(clock, 0, churn, 0)
         .Wire(song, 0, cellSize, 0)
         .Wire(bent, 0, colony, 0)
         .Wire(bent, 1, colony, 1)
         .Wire(churn, 0, colony, 2)
         .Wire(cellSize, 0, colony, 3)
         .Wire(colony, 1, wall, 2)
         .Wire(wall, 0, threads, 1)
         .Wire(colony, 2, cellDice, 0)
         .Wire(hatHits, 2, hatStep, 0)
         .Wire(cellDice, 0, cellPick, 0)
         .Wire(hatStep, 0, cellPick, 1)
         .Wire(cellPick, 0, cellRoll, 0)
         .Wire(cellRoll, 0, chosen, 1)
         .Wire(chosen, 0, sparkHit, 0)
         .Wire(hatHits, 0, sparkHit, 1)
         .Wire(sparkHit, 0, spark, 0)
         .Wire(hatsIn, 0, spark, 1);

        b.Group("Picture: Growth", boil, field, reach, bent, churn, cellSize, colony, wall, threads,
            cellDice, hatStep, cellPick, cellRoll, chosen, sparkHit, spark);

        // --- the picture: color ----------------------------------------------

        // Where in the palette to look: the field, the cell, a slow crawl and the
        // chord — so the harmony changing is seen as well as heard.
        var fieldHue = b.Add("math.mul", (1, 0.6f));
        var cellHue = b.Add("math.mul", (1, 0.35f));
        var placeHue = b.Add("math.add");
        var crawl = b.Add("math.mul", (1, 0.013f));
        var chordHue = b.Add("math.mul", (1, 0.04f));
        var timeHue = b.Add("math.add");
        var hueSum = b.Add("math.add");
        var where = b.Add("math.fract");

        // Tints of one color in the intro and the whole spectrum at the peak. It is
        // the palette's 'spread' rather than a saturation, so a quiet section is not a
        // grey one.
        var tint = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.08f), (4, 0.36f));
        var ground = b.Add(PaletteType, (1, 1.5f), (3, 0.45f));

        // Each cell darkens towards its wall, which is what makes it a body rather
        // than a tile.
        var shade = b.Add("math.remap", (1, 0f), (2, 1f), (3, 1.15f), (4, 0.2f));
        var body = b.Add("color.gain");

        // The threads take the far side of the same palette, so they are always the
        // complement of what they run through. The bass lights them, a sixteenth at a
        // time, and the hats' cells are added to the same ink.
        var across = b.Add("math.add", (1, 0.5f));
        var whereVein = b.Add("math.fract");
        var vein = b.Add(PaletteType, (3, 0.6f), (4, 0.4f));
        var bassSeen = b.Add("math.mul");
        var bassLit = b.Add("math.mul");
        var throb = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.55f), (4, 1.3f));
        var threadLit = b.Add("math.mul");
        var veinLevel = b.Add("math.add");
        var veinInk = b.Add("color.gain");

        // A loop drawn as wires: each pixel keeps nine tenths of what it held the
        // frame before unless the new frame is brighter. The threads move, and this is
        // where they were.
        var glow = b.Add("math.max");
        var fading = b.Add("color.gain", (1, 0.9f));
        var grown = Mode(b.Add(LayerType), "add");

        b.Wire(field, 0, fieldHue, 0)
         .Wire(colony, 2, cellHue, 0)
         .Wire(fieldHue, 0, placeHue, 0)
         .Wire(cellHue, 0, placeHue, 1)
         .Wire(clock, 0, crawl, 0)
         .Wire(shift, 0, chordHue, 0)
         .Wire(crawl, 0, timeHue, 0)
         .Wire(chordHue, 0, timeHue, 1)
         .Wire(placeHue, 0, hueSum, 0)
         .Wire(timeHue, 0, hueSum, 1)
         .Wire(hueSum, 0, where, 0)
         .Wire(song, 0, tint, 0)
         .Wire(where, 0, ground, 0)
         .Wire(tint, 0, ground, 2)
         .Wire(colony, 0, shade, 0)
         .Wire(ground, 0, body, 0)
         .Wire(shade, 0, body, 1)
         .Wire(where, 0, across, 0)
         .Wire(across, 0, whereVein, 0)
         .Wire(whereVein, 0, vein, 0)
         .Wire(tint, 0, vein, 2)
         .Wire(bassSeq, 1, bassSeen, 0)
         .Wire(pluck, 0, bassSeen, 1)
         .Wire(bassSeen, 0, bassLit, 0)
         .Wire(bassIn, 0, bassLit, 1)
         .Wire(bassLit, 0, throb, 0)
         .Wire(threads, 0, threadLit, 0)
         .Wire(throb, 0, threadLit, 1)
         .Wire(threadLit, 0, veinLevel, 0)
         .Wire(spark, 0, veinLevel, 1)
         .Wire(vein, 0, veinInk, 0)
         .Wire(veinLevel, 0, veinInk, 1)
         .Wire(veinInk, 0, glow, 0)
         .Wire(fading, 0, glow, 1)
         .Wire(glow, 0, fading, 0)
         .Wire(body, 0, grown, 0)
         .Wire(glow, 0, grown, 1);

        b.Group("Picture: Color", fieldHue, cellHue, placeHue, crawl, chordHue, timeHue, hueSum,
            where, tint, ground, shade, body, across, whereVein, vein, bassSeen, bassLit, throb,
            threadLit, veinLevel, veinInk, glow, fading, grown);

        // --- the picture: mandala --------------------------------------------

        // Turning against the plane behind it, and pushed by the same kick.
        var counter = b.Add("math.mul", (1, -0.11f));
        var turnBack = b.Add("space.rotate");
        var turned = b.Add("space.scale");

        // Six points on D, seven on E flat, four on C: the chord as a shape. The lead
        // sharpens them into needles while a note sounds.
        var points = b.Add("math.add", (1, 6f));
        var leadSeen = b.Add("math.mul");
        var sharpness = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.35f), (4, 0.75f));

        // The star with the polygon cut out of its heart, melted at the seam, and a
        // plain ring outside both. Only the outlines are drawn.
        var petals = b.Add(StarType, (2, 0.46f));
        var core = b.Add(PolygonType, (2, 0.2f));
        var halo = b.Add(CircleType, (2, 0.62f));
        var form = b.Add(CombineType, (2, 0.08f));
        var seal = b.Add(FillType, (1, 0.006f), (2, 0.014f));
        var ring = b.Add(FillType, (1, 0.006f), (2, 0.008f));
        var ringFaint = b.Add("math.mul", (1, 0.6f));
        var lines = b.Add("math.add");

        // The one thing here that listens rather than being told. A Meter reads
        // nothing in an exported still, which is why it is added to the gate's
        // brightness instead of standing in for it. Thirty milliseconds of window, so
        // it follows each hit.
        var thud = b.Add(NodeCatalog.MeterTypeId, (1, -1.5228788f));
        var felt = b.Add("math.mul", (1, 0.5f));
        var seen = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.55f), (4, 1.2f));
        var bright = b.Add("math.add");
        var linesLit = b.Add("math.mul");
        var aside = b.Add("math.add", (1, 0.33f));
        var whereSigil = b.Add("math.fract");
        var sigilHue = b.Add(PaletteType, (2, 0.3f), (3, 0.7f), (4, 0.3f));
        var sigil = b.Add("color.gain");

        b.Wire(clock, 0, counter, 0)
         .Wire(counter, 0, turnBack, 2)
         .Wire(turnBack, 0, turned, 0)
         .Wire(turnBack, 1, turned, 1)
         .Wire(pump, 0, turned, 2)
         .Wire(shift, 0, points, 0)
         .Wire(leadSeq, 1, leadSeen, 0)
         .Wire(leadIn, 0, leadSeen, 1)
         .Wire(leadSeen, 0, sharpness, 0)
         .Wire(turned, 0, petals, 0)
         .Wire(turned, 1, petals, 1)
         .Wire(points, 0, petals, 3)
         .Wire(sharpness, 0, petals, 4)
         .Wire(turned, 0, core, 0)
         .Wire(turned, 1, core, 1)
         .Wire(points, 0, core, 3)
         .Wire(petals, 0, form, 0)
         .Wire(core, 0, form, 1)
         .Wire(form, 2, seal, 0)
         .Wire(halo, 0, ring, 0)
         .Wire(ring, 1, ringFaint, 0)
         .Wire(seal, 1, lines, 0)
         .Wire(ringFaint, 0, lines, 1)
         .Wire(kickOut, 0, thud, 0)
         .Wire(thud, 1, felt, 0)
         .Wire(kickSeen, 0, seen, 0)
         .Wire(seen, 0, bright, 0)
         .Wire(felt, 0, bright, 1)
         .Wire(lines, 0, linesLit, 0)
         .Wire(bright, 0, linesLit, 1)
         .Wire(where, 0, aside, 0)
         .Wire(aside, 0, whereSigil, 0)
         .Wire(whereSigil, 0, sigilHue, 0)
         .Wire(sigilHue, 0, sigil, 0)
         .Wire(linesLit, 0, sigil, 1);

        b.Group("Picture: Mandala", counter, turnBack, turned, points, leadSeen, sharpness, petals,
            core, halo, form, seal, ring, ringFaint, lines, thud, felt, seen, bright, linesLit,
            aside, whereSigil, sigilHue, sigil);

        // --- the picture: ripples --------------------------------------------

        // The arp as rings: a higher note is a tighter ripple, struck by the pluck
        // and drawn only outside the star.
        var spacing = b.Add("math.remap", (1, 50f), (2, 86f), (3, 3f), (4, 13f));
        var outward = b.Add("math.mul", (1, -0.8f));
        var wave = b.Add("pattern.rings");
        var crest = b.Add("math.smoothstep", (0, 0.86f), (1, 1f));
        var outside = b.Add("math.smoothstep", (0, 0f), (1, 0.25f));
        var crestOut = b.Add("math.mul");
        var crestStruck = b.Add("math.mul");
        var crestGated = b.Add("math.mul");
        var crestIn = b.Add("math.mul");
        var ripple = b.Add("math.mul", (1, 0.7f));

        // Where each drip lands. The pattern's index through a sine and a cosine at
        // unrelated rates is hash enough for five positions.
        var dripTurnX = b.Add("math.mul", (1, 40f));
        var dripSin = b.Add("math.sin");
        var dripX = b.Add("math.mul", (1, 0.9f));
        var dripTurnY = b.Add("math.mul", (1, 23f));
        var dripCos = b.Add("math.cos");
        var dripY = b.Add("math.mul", (1, 0.6f));
        var dripAt = b.Add("space.translate");
        var dropShape = b.Add(CircleType, (2, 0.09f));
        var drop = b.Add(FillType);
        var dropStruck = b.Add("math.mul");
        var splash = b.Add("math.mul");
        var marks = b.Add("math.add");

        b.Wire(arpNote, 0, spacing, 0)
         .Wire(clock, 0, outward, 0)
         .Wire(spacing, 0, wave, 2)
         .Wire(outward, 0, wave, 3)
         .Wire(wave, 0, crest, 2)
         .Wire(petals, 0, outside, 2)
         .Wire(crest, 0, crestOut, 0)
         .Wire(outside, 0, crestOut, 1)
         .Wire(crestOut, 0, crestStruck, 0)
         .Wire(pluck, 0, crestStruck, 1)
         .Wire(crestStruck, 0, crestGated, 0)
         .Wire(arpGate, 0, crestGated, 1)
         .Wire(crestGated, 0, crestIn, 0)
         .Wire(arpIn, 0, crestIn, 1)
         .Wire(crestIn, 0, ripple, 0)
         .Wire(dripHits, 2, dripTurnX, 0)
         .Wire(dripTurnX, 0, dripSin, 0)
         .Wire(dripSin, 0, dripX, 0)
         .Wire(dripHits, 2, dripTurnY, 0)
         .Wire(dripTurnY, 0, dripCos, 0)
         .Wire(dripCos, 0, dripY, 0)
         .Wire(dripX, 0, dripAt, 2)
         .Wire(dripY, 0, dripAt, 3)
         .Wire(dripAt, 0, dropShape, 0)
         .Wire(dripAt, 1, dropShape, 1)
         .Wire(dropShape, 0, drop, 0)
         .Wire(drop, 1, dropStruck, 0)
         .Wire(dripHits, 0, dropStruck, 1)
         .Wire(dropStruck, 0, splash, 0)
         .Wire(padIn, 0, splash, 1)
         .Wire(ripple, 0, marks, 0)
         .Wire(splash, 0, marks, 1);

        b.Group("Picture: Ripples", spacing, outward, wave, crest, outside, crestOut, crestStruck,
            crestGated, crestIn, ripple, dripTurnX, dripSin, dripX, dripTurnY, dripCos, dripY,
            dripAt, dropShape, drop, dropStruck, splash, marks);

        // --- the picture: feedback -------------------------------------------

        // Two reads of the last frame. One is pulled towards the viewer, bent by the
        // field so the trail runs like liquid, and lurches on the kick; the other
        // backs away and turns against it.
        var drag = b.Add("space.warp", (3, 0.015f));
        var closer = b.Add("space.scale", (2, 1.02f));
        var lurch = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.004f), (4, 0.03f));
        var swirl = b.Add("space.rotate");
        var inward = b.Add("feedback");
        var warm = b.Add("color.split");
        var further = b.Add("space.scale", (2, 0.985f));
        var unswirl = b.Add("space.rotate", (2, -0.012f));
        var away = b.Add("feedback");
        var cool = b.Add("color.split");

        // Red from one and green and blue from the other, so the trail separates into
        // fringes as it ages. A long memory while the track is quiet and a short one
        // at the peak, where there is enough going on.
        var both = b.Add("color.rgb");
        var memory = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.93f), (4, 0.8f));
        var trail = b.Add("color.gain");

        b.Wire(field, 0, drag, 2)
         .Wire(drag, 0, closer, 0)
         .Wire(drag, 1, closer, 1)
         .Wire(kickHits, 0, lurch, 0)
         .Wire(closer, 0, swirl, 0)
         .Wire(closer, 1, swirl, 1)
         .Wire(lurch, 0, swirl, 2)
         .Wire(swirl, 0, inward, 0)
         .Wire(swirl, 1, inward, 1)
         .Wire(inward, 0, warm, 0)
         .Wire(further, 0, unswirl, 0)
         .Wire(further, 1, unswirl, 1)
         .Wire(unswirl, 0, away, 0)
         .Wire(unswirl, 1, away, 1)
         .Wire(away, 0, cool, 0)
         .Wire(warm, 0, both, 0)
         .Wire(cool, 1, both, 1)
         .Wire(cool, 2, both, 2)
         .Wire(song, 0, memory, 0)
         .Wire(both, 0, trail, 0)
         .Wire(memory, 0, trail, 1);

        b.Group("Picture: Feedback", drag, closer, lurch, swirl, inward, warm, further, unswirl,
            away, cool, both, memory, trail);

        // --- the picture, heard ----------------------------------------------

        // The picture, heard: a circle swept round the fractal field at the pad's
        // pitch, so the image is the wavetable and its boiling is the timbre. Where
        // the circle sits drifts with the weather.
        var readAt = b.Add("math.remap", (1, 0f), (2, 1f), (3, -0.5f), (4, 0.5f));
        var reader = b.Add(NodeCatalog.ScanTypeId, (3, 0.35f));

        // A one-pole lowpass made of a Mix and a wire back into it: each sample is
        // mostly the one before. A twentieth the cost of a Filter, for a voice that
        // needs only its top taken off.
        var heard = b.Add("math.mix", (2, 0.045f));
        var scanOut = b.Add("math.mul");

        b.Wire(weather, 0, readAt, 0)
         .Wire(field, 1, reader, 0)
         .Wire(padHz, 0, reader, 2)
         .Wire(readAt, 0, reader, 4)
         .Wire(heard, 0, heard, 0)
         .Wire(reader, 0, heard, 1)
         .Wire(heard, 0, scanOut, 0)
         .Wire(scanIn, 0, scanOut, 1);

        b.Group("Picture Voice", readAt, reader, heard, scanOut);

        // --- the riser -------------------------------------------------------

        // The same hiss through a band that climbs nearly five octaves in four bars.
        // The ramp that moves the band also opens it.
        var sweepCut = b.Add("math.remap", (1, 0f), (2, 1f), (3, 250f), (4, 7000f));
        var sweep = b.Add(FilterType, (2, 0.55f));
        var sweepIn = b.Add("math.mul");
        var riserOut = b.Add("math.mul", (1, 1.6f));

        b.Wire(ramp, 0, sweepCut, 0)
         .Wire(hiss, 0, sweep, 0)
         .Wire(sweepCut, 0, sweep, 1)
         .Wire(sweep, 1, sweepIn, 0)
         .Wire(ramp, 0, sweepIn, 1)
         .Wire(sweepIn, 0, riserOut, 0);

        b.Group("Riser", sweepCut, sweep, sweepIn, riserOut);

        // --- the dub echo ----------------------------------------------------

        // What goes in: the arp, the drips, the lead, and a little of the snare.
        var send = b.Add("math.mixer", (1, 0.8f), (3, 0.7f), (5, 0.6f), (7, 0.2f));

        // A feedback loop drawn rather than dialed. Both Delays have their own
        // feedback at nothing, and the repeats come round through the wire at the
        // bottom of this group instead — which is what lets something be done to them
        // on the way. Three sixteenths then two, worked out from the tempo, so the
        // left tap is a dotted eighth and the right lands on the beat after it.
        var echoIn = b.Add("math.add");
        var dotted = b.Add("math.div", (0, 3f));
        var straight = b.Add("math.div", (0, 2f));
        var tapL = b.Add(DelayModule.TypeId, (2, 0f), (3, 1f));
        var tapR = b.Add(DelayModule.TypeId, (2, 0f), (3, 1f));

        // Each time round, a lowpass made the way the one above is, a Drive, and a
        // little over half the level: every repeat is darker and rounder than the
        // last, which is a tape echo.
        var dark = b.Add("math.mix", (2, 0.06f));
        var worn = b.Add(DriveType, (1, 1.5f));
        var returned = b.Add("math.mul", (1, 0.55f));

        b.Wire(arpOut, 0, send, 0)
         .Wire(dripOut, 0, send, 2)
         .Wire(leadOut, 0, send, 4)
         .Wire(snareOut, 0, send, 6)
         .Wire(send, 0, echoIn, 0)
         .Wire(returned, 0, echoIn, 1)
         .Wire(sixteenths, 0, dotted, 1)
         .Wire(sixteenths, 0, straight, 1)
         .Wire(echoIn, 0, tapL, 0)
         .Wire(dotted, 0, tapL, 1)
         .Wire(tapL, 0, tapR, 0)
         .Wire(straight, 0, tapR, 1)
         .Wire(dark, 0, dark, 0)
         .Wire(tapR, 0, dark, 1)
         .Wire(dark, 0, worn, 0)
         .Wire(worn, 0, returned, 0);

        b.Group("Dub Echo", send, echoIn, dotted, straight, tapL, tapR, dark, worn, returned);

        // --- the room --------------------------------------------------------

        // One Reverb on a send, fully wet, fed by what should sound far away. 'out'
        // and 'wide' are the two sides.
        var roomSend = b.Add("math.mixer", (1, 0.5f), (3, 0.5f), (5, 0.8f), (7, 0.8f));
        var snareSend = b.Add("math.mul", (1, 0.3f));
        var roomIn = b.Add("math.add");
        var room = b.Add(ReverbModule.TypeId, (1, 0.85f), (2, 0.8f), (3, 1f));

        b.Wire(wide, 0, roomSend, 0)
         .Wire(tapL, 0, roomSend, 2)
         .Wire(dripOut, 0, roomSend, 4)
         .Wire(scanOut, 0, roomSend, 6)
         .Wire(snareOut, 0, snareSend, 0)
         .Wire(roomSend, 0, roomIn, 0)
         .Wire(snareSend, 0, roomIn, 1)
         .Wire(roomIn, 0, room, 0);

        b.Group("Room", roomSend, snareSend, roomIn, room);

        // --- the desk --------------------------------------------------------

        // The kick and the bass are summed first because the duck ties them into one
        // instrument, and everything else arrives in buses for the four channels a
        // Mixer has. Left and right differ in which echo tap and which side of the
        // Chorus and the Reverb they carry, and in how the arp and the drips lean.
        var lowEnd = b.Add("math.mixer", (1, 0.9f), (3, 0.7f));
        var top = b.Add("math.mixer", (1, 0.85f), (3, 0.4f), (5, 0.5f));
        var musicL = b.Add("math.mixer", (1, 0.5f), (3, 0.45f), (5, 0.5f), (7, 0.35f));
        var musicR = b.Add("math.mixer", (1, 0.5f), (3, 0.3f), (5, 0.5f), (7, 0.55f));
        var fxL = b.Add("math.mixer", (1, 0.55f), (3, 0.5f), (5, 0.4f));
        var fxR = b.Add("math.mixer", (1, 0.55f), (3, 0.5f), (5, 0.4f));
        var deskL = b.Add("math.mixer");
        var deskR = b.Add("math.mixer");

        // A trim under unity and a Clamp that should never be reached. No Drive on the
        // way out: a saturator here brings the quiet parts up into the kick and the
        // sub, and that is heard as distortion rather than as loudness.
        var trimL = b.Add("math.mul", (1, 0.9f));
        var trimR = b.Add("math.mul", (1, 0.9f));
        var safeL = b.Add("math.clamp", (1, -1f), (2, 1f));
        var safeR = b.Add("math.clamp", (1, -1f), (2, 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.7f));

        b.Wire(kickOut, 0, lowEnd, 0)
         .Wire(bassOut, 0, lowEnd, 2)
         .Wire(snareOut, 0, top, 0)
         .Wire(hatOut, 0, top, 2)
         .Wire(riserOut, 0, top, 4)
         .Wire(wide, 0, musicL, 0)
         .Wire(arpOut, 0, musicL, 2)
         .Wire(leadOut, 0, musicL, 4)
         .Wire(dripOut, 0, musicL, 6)
         .Wire(wide, 1, musicR, 0)
         .Wire(arpOut, 0, musicR, 2)
         .Wire(leadOut, 0, musicR, 4)
         .Wire(dripOut, 0, musicR, 6)
         .Wire(tapL, 0, fxL, 0)
         .Wire(room, 0, fxL, 2)
         .Wire(scanOut, 0, fxL, 4)
         .Wire(tapR, 0, fxR, 0)
         .Wire(room, 1, fxR, 2)
         .Wire(scanOut, 0, fxR, 4)
         .Wire(lowEnd, 0, deskL, 0)
         .Wire(top, 0, deskL, 2)
         .Wire(musicL, 0, deskL, 4)
         .Wire(fxL, 0, deskL, 6)
         .Wire(lowEnd, 0, deskR, 0)
         .Wire(top, 0, deskR, 2)
         .Wire(musicR, 0, deskR, 4)
         .Wire(fxR, 0, deskR, 6)
         .Wire(deskL, 0, trimL, 0)
         .Wire(deskR, 0, trimR, 0)
         .Wire(trimL, 0, safeL, 0)
         .Wire(trimR, 0, safeR, 0)
         .Wire(safeL, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(safeR, 0, output, NodeCatalog.OutputRightPort);

        b.Group("Desk", lowEnd, top, musicL, musicR, fxL, fxR, deskL, deskR, trimL, trimR, safeL,
            safeR);

        // --- the picture: print ----------------------------------------------

        // The growth, then the mandala screened over it, the ripples and splashes
        // added, and the Scan's own trace laid on whenever its voice is in.
        var stamped = Mode(b.Add(LayerType), "screen");
        var marked = b.Add("math.add");
        var traced = Mode(b.Add(LayerType), "add");

        // Here for 'radius', the one Coordinates output nothing is normalled to, which
        // darkens the corners.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var edgeFall = b.Add("math.remap", (1, 0.3f), (2, 1.6f), (3, 1f), (4, 0.35f));
        var vignette = b.Add("math.clamp", (1, 0f), (2, 1f));
        var fresh = b.Add("color.gain");

        // Maximum rather than a blend, so a trail brighter than the new frame keeps
        // its brightness and reads as a streak.
        var combined = b.Add("math.max");

        // The riser, seen: the finished frame folded back on itself as the ramp
        // climbs. The Feedback reads what was printed, so the creases go round the
        // loop until the section lands.
        var strain = b.Add("math.mul", (1, 1.5f));
        var foldDrive = b.Add("math.add", (1, 1f));
        var creased = b.Add(FoldType);

        // And graded by the section, last, so the intro is muted and the peak is
        // not.
        var richness = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.85f), (4, 1.35f));
        var graded = b.Add(GradeType, (2, 1.12f), (3, 1.15f));

        b.Wire(grown, 0, stamped, 0)
         .Wire(sigil, 0, stamped, 1)
         .Wire(stamped, 0, marked, 0)
         .Wire(marks, 0, marked, 1)
         .Wire(marked, 0, traced, 0)
         .Wire(reader, 1, traced, 1)
         .Wire(scanIn, 0, traced, 2)
         .Wire(coord, 2, edgeFall, 0)
         .Wire(edgeFall, 0, vignette, 0)
         .Wire(traced, 0, fresh, 0)
         .Wire(vignette, 0, fresh, 1)
         .Wire(trail, 0, combined, 0)
         .Wire(fresh, 0, combined, 1)
         .Wire(ramp, 0, strain, 0)
         .Wire(strain, 0, foldDrive, 0)
         .Wire(combined, 0, creased, 0)
         .Wire(foldDrive, 0, creased, 1)
         .Wire(song, 0, richness, 0)
         .Wire(creased, 0, graded, 0)
         .Wire(richness, 0, graded, 1)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Picture: Print", stamped, marked, traced, coord, edgeFall, vignette, fresh,
            combined, strain, foldDrive, creased, richness, graded);

        return b.Build();
    }
}
