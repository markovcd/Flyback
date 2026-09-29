using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Flyback's own theme: three minutes of synthwave at a hundred and twenty-eight in A
/// minor, the promo's arp, bass and hook grown into a song, on a two-channel scope
/// whose beam sweeps the screen once a beat.
/// </summary>
/// <remarks>
/// The harmony is four roots a bar, A, F, C and G, then A, F, A and G. Every chord
/// tone is a root plus an interval snapped to A minor, so the arp and the pads never
/// name a chord. On the screen the upper trace is the bass's sawtooth, as many teeth
/// as its root is high, and its missing vertical edges are the retrace the beam is
/// blanked for; the lower is the arp's sine.
/// </remarks>
internal sealed class ThemePreset : PresetBench
{
    public const string Name = "Flyback Theme";

    private const string Voice = "flyback.voice";

    private const string Picture = "flyback.picture";

    private const string Mastering = "flyback.mastering";

    private const string SupersawType = "flyback.voice.osc";

    private const string SlewType = NodeCatalog.SlewTypeId;

    private const string FilterType = NodeCatalog.FilterTypeId;

    private const string FillType = "flyback.picture.fill";

    private const string TextType = "flyback.picture.text";

    private const string CompressorType = "flyback.mastering.compressor";

    private const string EqType = "flyback.mastering.eq";

    private const string WidthType = "flyback.mastering.width";

    private const string LimiterType = "flyback.mastering.limiter";

    /// <summary>A natural minor: A, B, C, D, E, F and G.</summary>
    private static readonly int[] Scale = [9, 11, 0, 2, 4, 5, 7];

    /// <summary>Between a minor third and a major one: the scale picks which.</summary>
    private const float Third = 3.5f;

    private const int Kick = 0, Hats = 1, Bass = 2, Arp = 3, Chords = 4, Lead = 5, Swell = 6, Pops = 7;

    public static Patch Build(ModuleCatalog modules)
    {
        foreach (var plugin in new[] { Voice, Picture, Mastering })
        {
            if (!modules.HasProvider(plugin))
                throw new InvalidOperationException($"it needs the {plugin} plugin, which is not installed.");
        }

        return new ThemePreset(modules).Assemble();
    }

    private ThemePreset(ModuleCatalog modules)
        : base(modules)
    {
    }

    private Patch Assemble()
    {
        // --- the clock -------------------------------------------------------

        var beat = b.Add(NodeCatalog.TempoTypeId, (0, 128f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = Product(clock, beat);

        Box("Clock");

        // --- the arrangement -------------------------------------------------

        // Twelve sections of eight bars: intro, intro, verse, verse, chorus, chorus,
        // breakdown, build, three choruses and the outro.
        var parts = Arranged(beats, 1f / 32f,
        [
            Levels(0, 0, 0.85f, 0.85f, 1, 1, 0, 0.85f, 1, 1, 1, 0),
            Levels(0, 0.4f, 0.7f, 1, 1, 1, 0, 0.7f, 1, 1, 1, 0),
            Levels(0, 0.5f, 0.85f, 0.85f, 1, 1, 0, 0.85f, 1, 1, 1, 0),
            Levels(0.3f, 0.35f, 0.8f, 0.8f, 1, 1, 0.25f, 0.8f, 1, 1, 1, 0.25f),
            Levels(0.35f, 0.3f, 0.3f, 0.35f, 0.3f, 0.3f, 0.2f, 0.35f, 0.3f, 0.3f, 0.35f, 0.3f),
            Levels(0, 0, 0, 0, 1, 1, 0.35f, 0, 1, 1, 1, 0),

            // How far the filters open.
            Levels(0.3f, 0.4f, 0.55f, 0.65f, 1, 1, 0.45f, 0.65f, 1, 1, 1, 0.35f),

            Levels(0, 0.7f, 1, 1, 1, 1, 0.6f, 1, 1, 1, 1, 0.8f),
        ]);

        // Filters sweep open over four seconds and close over two.
        var swell = b.Add(SlewType, (1, 0.60206f), (2, 0.30103f));

        // The verse and the build end in a riser, a snare roll through their last bar.
        var turns = Formula("step(3, (a - 1) % 4) * step(a, 8)", new Read(parts, SectionNumber));
        var ramp = Product(Rises(parts, 0.5f, 1f, SectionProgress), turns);
        var roll = Formula("step(0.875, a) * (a * 8 - 7) * b", new Read(parts, SectionProgress), turns);

        // A bar a step.
        var root = b.Add("seq.notes", (1, 0.25f));
        StepsExtra.Set(root,
        [
            new Step(45f), new Step(41f), new Step(48f), new Step(43f),
            new Step(45f), new Step(41f), new Step(45f), new Step(43f),
        ]);

        b.Wire(parts, Swell, swell, 0)
         .Wire(beats, 0, root, 0);

        Box("Arrangement");

        // --- the drums -------------------------------------------------------

        var kickStroke = Product(Stroke(beats, 1f, 5f), parts, Kick);
        var kick = Drum(kickStroke, 46f, 130f, 4f, 3.5f);

        // Two and four.
        var clapStroke = Product(Stroke(beats, 0.5f, 7f, 0.5f), parts, Kick);
        var clap = Sum(Hiss(clapStroke, 1800f, 0.25f, "band", 2.5f, seed: 1f), Drum(clapStroke, 195f, 0f, 1f, 0f));

        // Ghost sixteenths under an open hat on the off-beat.
        var hatStroke = Product(
            Sum(Times(Stroke(beats, 4f, 12f), 0.35f), Times(Stroke(beats, 1f, 4f, 0.5f), 0.8f)),
            parts, Hats);
        var hats = Hiss(hatStroke, 7500f, 0.2f, "high", seed: 2f);

        var rollStroke = Product(Stroke(beats, 4f, 5f), roll);
        var snare = Sum(Hiss(rollStroke, 1900f, 0.3f, "band", 2.5f, seed: 3f), Drum(rollStroke, 185f, 0f, 1f, 0f));

        // Every section the kick plays in opens on a crash and a sub drop.
        var crashStroke = Product(Stroke(beats, 1f / 32f, 12f), parts, Kick);
        var crash = Sum(Hiss(crashStroke, 4200f, 0.1f, "high", seed: 4f), Drum(crashStroke, 32f, 40f, 1f, 0.5f));

        Box("Drums");

        // --- the bass --------------------------------------------------------

        // Root and octave in eighths.
        var bassLine = b.Add("seq.values", (1, 2f), (2, 0.92f), (3, 0.03f));
        StepsExtra.Set(bassLine, [new Step(0f), new Step(12f)]);
        var bassSaw = b.Add("osc.saw", (3, 0.7f));
        var bassTone = b.Add(FilterType, (2, 0.25f));
        var sub = b.Add("osc.sine", (3, 0.6f));
        var bassPluck = Stroke(beats, 2f, 6f);
        var bass = Product(Product(Sum(bassTone, sub), bassLine, 1), parts, Bass);

        b.Wire(beats, 0, bassLine, 0)
         .Wire(Through("audio.note", Plus(Sum(bassLine, root), -12f)), 0, bassSaw, 1)
         .Wire(bassSaw, 0, bassTone, 0)
         .Wire(Formula("110 + 1700 * a * b", bassPluck, Span(swell, 0f, 1f, 0.6f, 1f)), 0, bassTone, 1)
         .Wire(Through("audio.note", Plus(root, -12f)), 0, sub, 1);

        Box("Bass");

        // --- the arp ---------------------------------------------------------

        // Up the chord to the tenth and back, in sixteenths, an octave higher once the
        // filters are open. Four voices take the sixteenths in turn and each rings for
        // a beat, so a note is still sounding under the three after it.
        float[] steps = [0f, Third, 7f, 12f, 12f + Third, 12f, 7f, Third];
        float[] accents = [1f, 0.55f, 0.72f, 0.55f];
        var octave = Formula("12 + 12 * step(0.9, a)", swell);
        var brightness = Span(swell, 0f, 1f, 900f, 6500f);
        var arpVoices = new NodeInstance[4];
        var arpStrokes = new NodeInstance[4];
        var arpHz = default(NodeInstance)!;

        for (var voice = 0; voice < 4; voice++)
        {
            var turn = voice / 4f;
            var line = b.Add("seq.values", (1, 1f));
            StepsExtra.Set(line, [new Step(steps[voice]), new Step(steps[voice + 4])]);

            var hz = InKey(Sum(line, root), Scale);
            var saw = b.Add("osc.saw", (3, 0.6f));
            var twin = b.Add("osc.saw", (3, 0.4f));
            var voiceTone = b.Add(FilterType, (2, 0.35f));
            var level = Times(Stroke(beats, 1f, 2.1f, turn), accents[voice]);

            b.Wire(Plus(beats, -turn), 0, line, 0)
             .Wire(octave, 0, hz, 1)
             .Wire(hz, 0, saw, 1)
             .Wire(Times(hz, 1.0045f), 0, twin, 1)
             .Wire(Product(Sum(saw, twin), level), 0, voiceTone, 0)
             .Wire(Formula("180 + a * b", Stroke(beats, 1f, 4.3f, turn), brightness), 0, voiceTone, 1);

            arpVoices[voice] = voiceTone;
            arpStrokes[voice] = level;
            if (voice == 0) arpHz = hz;
        }

        var arp = Product(Sum(Sum(arpVoices[0], arpVoices[1]), Sum(arpVoices[2], arpVoices[3])), parts, Arp);
        var arpStroke = Wired("math.max", Wired("math.max", arpStrokes[0], arpStrokes[1]), Wired("math.max", arpStrokes[2], arpStrokes[3]));

        Box("Arp");

        // --- the chords ------------------------------------------------------

        // Three supersaws on the root, third and fifth: a pad while the filters are
        // shut, and pumping stabs under the kick once they open.
        var padNote = Plus(root, 12f);
        var low = b.Add(SupersawType, (2, 0.3f), (3, 0.75f), (5, 0.35f));
        var middle = b.Add(SupersawType, (2, 0.3f), (3, 0.75f), (5, 0.35f));
        var high = b.Add(SupersawType, (2, 0.3f), (3, 0.75f), (5, 0.35f));
        var padTone = b.Add(FilterType, (2, 0.15f));
        var pad = b.Add(ChorusModule.TypeId, (1, 0.25f), (2, 0.6f), (3, 0.6f));
        var chords = Product(pad, parts, Chords);
        var chordsWide = Wired("math.mul", pad, parts, 1, Chords);

        b.Wire(Through("audio.note", padNote), 0, low, 1)
         .Wire(InKey(padNote, Scale, Third), 0, middle, 1)
         .Wire(InKey(padNote, Scale, 7f), 0, high, 1)
         .Wire(Sum(Sum(low, middle), high), 0, padTone, 0)
         .Wire(Span(swell, 0f, 1f, 1100f, 9000f), 0, padTone, 1)
         .Wire(padTone, 0, pad, 0);

        Box("Chords");

        // --- the lead --------------------------------------------------------

        // The promo's hook, in eighths over four bars.
        var leadLine = b.Add("seq.notes", (1, 2f), (2, 0.9f), (3, 0.06f));
        StepsExtra.Set(leadLine,
        [
            new Step(76f, 2f), new Step(81f), new Step(79f), new Step(76f, 2f), new Step(74f), new Step(72f),
            new Step(72f), new Step(74f), new Step(76f, 2f), new Step(77f), new Step(76f, 0.5f), new Step(76f, 2.5f, 0f),
            new Step(81f, 3f), new Step(79f), new Step(76f, 2f), new Step(74f), new Step(76f),
            new Step(76f, 5f), new Step(76f, 3f, 0f),
        ]);

        var glide = b.Add(SlewType, (1, -1.4f), (2, -1.4f));
        var leadHz = Through("audio.note", glide);
        var vibrato = b.Add("osc.sine", (1, 5.2f), (3, 0.12f));
        var leadSaw = b.Add("osc.saw", (3, 0.5f));
        var leadTwin = b.Add("osc.saw", (3, 0.5f));
        var leadTone = b.Add(FilterType, (1, 3400f), (2, 0.3f));
        var tongue = b.Add(SlewType, (1, -2.5f), (2, -1.2f));
        var lead = Product(Product(leadTone, tongue), parts, Lead);

        b.Wire(beats, 0, leadLine, 0)
         .Wire(leadLine, 0, glide, 0)
         .Wire(leadHz, 0, leadSaw, 1)
         .Wire(vibrato, 0, leadSaw, 2)
         .Wire(Times(leadHz, 1.006f), 0, leadTwin, 1)
         .Wire(Sum(leadSaw, leadTwin), 0, leadTone, 0)
         .Wire(leadLine, 1, tongue, 0);

        Box("Lead");

        // --- the pops --------------------------------------------------------

        // The hook: seven eighths up A minor pentatonic, A C D E G A C, on the last bar
        // of every four, climbing into the next phrase. Two voices take the eighths in
        // turn, each a sine with its octave and twelfth, ringing over the next note and
        // starting half again as high for its first few milliseconds.
        float[][] runs = [[69f, 74f, 79f, 84f], [72f, 76f, 81f]];
        var pops = new NodeInstance[2];

        for (var voice = 0; voice < 2; voice++)
        {
            var turn = voice / 2f;
            var line = b.Add("seq.notes", (1, 1f), (2, 1f), (3, 0.02f));
            var run = new List<Step> { new(runs[voice][0], 12f, 0f) };
            run.AddRange(runs[voice].Select(note => new Step(note)));
            if (run.Count < 5) run.Add(new Step(runs[voice][^1], 1f, 0f));
            StepsExtra.Set(line, run);

            var level = Product(Product(Stroke(beats, 1f, 2.9f, turn), line, 1), parts, Pops);
            var hz = Formula("a * (1 + 0.5 * b)", Through("audio.note", line), Stroke(beats, 1f, 78f, turn));

            b.Wire(Plus(beats, -turn), 0, line, 0);

            pops[voice] = Sum(
                Tone(hz, level),
                Sum(Tone(Times(hz, 2f), Times(level, 0.35f)), Tone(Times(hz, 3f), Times(level, 0.12f))));
        }

        var pop = Sum(pops[0], pops[1]);

        Box("Pops");

        // --- the riser -------------------------------------------------------

        var riser = Hiss(ramp, 300f, 0.55f, "band", 1.4f, seed: 5f);

        b.Wire(Span(ramp, 0f, 1f, 300f, 9000f), 0, riser, HissCutoff);

        Box("Riser");

        // --- the space -------------------------------------------------------

        // A ping-pong of three sixteenths a side, and a hall.
        var send = b.Add("math.mixer", (1, 0.5f), (3, 0.35f), (5, 0.5f));
        var taps = Echo(send, beat, 3f, 3f, 0.42f, 1f);
        var roomSend = b.Add("math.mixer", (1, 0.35f), (3, 0.2f), (5, 0.25f), (7, 0.3f));
        var room = b.Add(NodeCatalog.ReverbTypeId, (1, 0.8f), (2, 0.75f), (3, 1f));

        b.Wire(arp, 0, send, 0)
         .Wire(lead, 0, send, 2)
         .Wire(pop, 0, send, 4)
         .Wire(clap, 0, roomSend, 0)
         .Wire(lead, 0, roomSend, 2)
         .Wire(chords, 0, roomSend, 4)
         .Wire(snare, 0, roomSend, 6)
         .Wire(roomSend, 0, room, 0);

        Box("Space");

        // --- the desk --------------------------------------------------------

        var music = b.Add(DeskType);
        var space = b.Add(DeskType);

        Channel(music, 1, 0.45f, bass);
        Channel(music, 2, 0.13f, arp, Times(arp, 0.8f));
        Channel(music, 3, 1f, chords, chordsWide);
        Channel(music, 4, 0.25f, lead);

        Channel(space, 1, 0.3f, taps, taps, rightFrom: EchoRight);
        Channel(space, 2, 0.35f, room, room, rightFrom: 1);
        Channel(space, 3, 0.25f, riser);
        Channel(space, 4, 0.22f, pop);

        // The dry synths duck under the kick; their echoes and the hall do not.
        var ducked = Ducking(kickStroke, 0.72f);

        b.Wire(music, 0, ducked, 0)
         .Wire(music, 1, ducked, 1);

        var drums = b.Add(DeskType);
        var master = b.Add(DeskType, (DeskTrim, 0.75f));

        Channel(drums, 1, 0.65f, kick);
        Channel(drums, 2, 0.45f, Sum(clap, snare));
        Channel(drums, 3, 0.8f, hats);
        Channel(drums, 4, 0.3f, crash);

        Channel(master, 1, 1f, ducked, ducked, rightFrom: 1);

        Chained(drums, space, master);

        Box("Desk");

        // --- the master ------------------------------------------------------

        // Glued a few decibels, as the promo's saturation does, then brightened.
        var glue = b.Add(CompressorType, (3, -10f), (4, 2f), (5, -1.5f), (6, -0.9f), (8, 8f));
        var tone = b.Add(EqType, (2, 30f), (5, 350f), (6, -3f), (7, 0.8f), (8, 6000f), (9, 6f));
        var wide = b.Add(WidthType, (2, 1.25f), (3, 150f));
        var loud = b.Add(LimiterType, (2, -1f), (3, -1.3f));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.8f));

        b.Wire(master, 0, glue, 0)
         .Wire(master, 1, glue, 1)
         .Wire(glue, 0, tone, 0)
         .Wire(glue, 1, tone, 1)
         .Wire(tone, 0, wide, 0)
         .Wire(tone, 1, wide, 1)
         .Wire(wide, 0, loud, 0)
         .Wire(wide, 1, loud, 1)
         .Wire(loud, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(loud, 1, output, NodeCatalog.OutputRightPort);

        Box("Master", output);

        // --- the picture: scope ----------------------------------------------

        // The beam crosses the screen once a beat, and the phosphor it leaves fades
        // over the beat after.
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var sweep = Formula("exp(-3 * fract(a - (b / c + 1) / 2))", beats, coord, new Read(coord, 4));

        // Channel one, the bass: a sawtooth with a tooth for every 160 hertz of root,
        // riding the kick.
        var bassTeeth = Formula(
            "a * b / 160 + c / 4",
            coord,
            Through("audio.note", root),
            beats);
        var bassTrace = Formula(
            "0.005 / (abs(b - 0.35 - c * (2 * fract(a) - 1)) + 0.005)",
            bassTeeth,
            new Read(coord, 1),
            Formula("0.06 + 0.16 * a + 0.1 * b", swell, kickStroke));

        // Channel two, the arp: a sine for every 250 hertz of its note, struck by it.
        var arpCycles = Formula("a * b / 250", coord, arpHz);
        var arpTrace = Formula(
            "0.004 / (abs(b + 0.45 - c * sin(a * tau)) + 0.004)",
            arpCycles,
            new Read(coord, 1),
            Formula("0.04 + 0.16 * a * b", arpStroke, new Read(parts, Arp)));

        // The graticule, lit by the hats.
        var graticule = Formula(
            "max(smoothstep(0.46, 0.49, abs(fract(a * 2) - 0.5)), smoothstep(0.46, 0.49, abs(fract(b * 2) - 0.5))) * (0.1 + 0.25 * c)",
            coord, new Read(coord, 1), hatStroke);

        // The name, typed out across the first bar of the first chorus of each run, the
        // peak and the outro.
        var name = b.Add(TextType, (2, 0.24f));
        name.SetState("text", new JsonObject { ["lines"] = "FLYBACK", ["font"] = "pixel" });
        var nameInk = b.Add(FillType, (1, 0.004f));
        var shown = Formula(
            "max(step(4.5, a) * step(a, 5.5), max(step(8.5, a) * step(a, 9.5), step(10.5, a))) * (0.7 + 0.5 * b)",
            new Read(parts, SectionNumber), kickStroke);

        b.Wire(Rises(parts, 0f, 0.125f, SectionProgress), 0, name, 4)
         .Wire(name, 0, nameInk, 0);

        Box("Picture: Scope");

        // --- the picture: tube -----------------------------------------------

        var glass = b.Add("color.rgb", (0, 0.01f), (1, 0.015f), (2, 0.03f));
        var withGrid = Ink(glass, graticule, 0.15f, 0.45f, 0.5f);
        var withBass = Ink(withGrid, Product(bassTrace, Plus(Times(sweep, 0.8f), 0.2f)), 0.3f, 1f, 0.75f);
        var withArp = Ink(withBass, Product(arpTrace, Plus(Times(sweep, 0.8f), 0.2f)), 1f, 0.35f, 0.7f);
        var withName = Ink(withArp, Product(nameInk, shown), 1f, 0.9f, 0.74f);

        var phosphor = b.Add(TrailsType, (TrailsZoom, 1.004f), (TrailsPersist, 0.72f));
        var scanned = b.Add("color.gain");
        var shaded = Vignette(scanned, 0.6f, 2.1f, 0.5f);
        var graded = b.Add("color.gain");

        b.Wire(withName, 0, phosphor, 0)
         .Wire(phosphor, 0, scanned, 0)
         .Wire(Formula("0.85 + 0.15 * sin(a * 500)", new Read(coord, 1)), 0, scanned, 1)
         .Wire(shaded, 0, graded, 0)
         .Wire(Span(swell, 0f, 1f, 0.8f, 1.3f), 0, graded, 1)
         .Wire(graded, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture: Tube");

        return b.Build();
    }
}
