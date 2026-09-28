using System.Globalization;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Easy;

/// <summary>
/// A whole acid house track on the two Easy modules: ninety-six bars of a drum machine,
/// a bass line and a chord stab over A minor, D minor, A minor and E minor, with an organ hook on top.
/// </summary>
/// <remarks>
/// One lane stepping once every eight bars says how much of the track is playing, and
/// every part reads its own entry from it: the intro, a build, the first drop, a
/// breakdown where the stab turns into a pad under a soft hook, a second
/// build, the last drop with the hook an octave up, and the way out, which leads back
/// into the start. A build is the lane at a half: a snare roll climbs through its second
/// half and the kick sits out its last bar. The chord changes every two bars and
/// transposes the bass line, as the machine itself transposes a pattern, and a coin picks
/// each pass of the hook from it and two variations. The drums keep
/// their own time at 124 bpm and every lane steps on Time at the same tempo, so nothing
/// is wired for timing. The picture is rings bent by folded clouds, colored by the chord
/// and folded, bent and turned differently in each phrase, with the kick punching the zoom
/// and trails carrying it rather than a flash.
/// </remarks>
internal static class WarehousePreset
{
    public const string Name = "Warehouse";

    private const float Bpm = 124f;

    /// <summary>Sixteenths a second at <see cref="Bpm"/>, which is what the drums count in.</summary>
    private const float Sixteenths = Bpm / 15f;

    /// <summary>Bars a second.</summary>
    private const float Bars = Bpm / 240f;

    /// <summary>Chords a second: one every two bars.</summary>
    private const float Chords = Bars / 2f;

    /// <summary>Phrases a second: one every eight bars, which is one step of the arrangement.</summary>
    private const float Phrases = Bars / 8f;

    /// <summary>
    /// How much of the track plays in each phrase: intro, intro with the bass, build, two
    /// of the first drop, breakdown, build, two of the last drop, stripped back, and out.
    /// </summary>
    private static readonly float[] Song = [0.1f, 0.3f, 0.5f, 0.8f, 0.85f, 0.05f, 0.55f, 1f, 1f, 0.4f, 0.3f, 0.1f];

    /// <summary>How far each bass note's pluck opens its filter in each phrase.</summary>
    private static readonly float[] Plucks = [0.1f, 0.12f, 0.18f, 0.25f, 0.28f, 0.2f, 0.3f, 0.35f, 0.35f, 0.25f, 0.15f, 0.1f];

    /// <summary>The chords' roots in semitones from A, which the bass line is moved by.</summary>
    private static readonly int[] Roots = [0, 5, 0, -5];

    /// <summary>The stab's three voices, one chord each: A minor, D minor, A minor, E minor, voiced close.</summary>
    private static readonly int[][] Voices = [[57, 57, 57, 55], [60, 62, 60, 59], [64, 65, 64, 64]];

    /// <summary>How many wedges the picture is folded into in each phrase.</summary>
    private static readonly float[] Folds = [3f, 4f, 5f, 6f, 8f, 3f, 5f, 7f, 12f, 6f, 4f, 3f];

    /// <summary>How far the fold turns on each bar of a phrase, in radians; the drops turn one way, then back.</summary>
    private static readonly float[] Twists = [0f, 0.1f, 0.2f, 0.3f, -0.3f, 0f, 0.25f, 0.4f, -0.4f, 0.2f, 0.1f, 0f];

    /// <summary>How hard the clouds bend the rings in each phrase.</summary>
    private static readonly float[] Bends = [0.5f, 0.8f, 1f, 1.4f, 2f, 2.5f, 1.2f, 1.6f, 2.2f, 1f, 0.7f, 0.5f];

    /// <summary>Each chord's hue, and the next one's, turned toward it over the chord's last half bar.</summary>
    private static readonly float[] Hues = [0.6f, 0.72f, 0.6f, 0.5f];

    private static readonly float[] NextHues = [0.72f, 0.6f, 0.5f, 0.6f];

    /// <summary>
    /// Two bars of the bass on A, a note number or nought for a rest: on the offbeats between
    /// the kicks, with a pick-up at the end of each bar.
    /// </summary>
    private static readonly int[] Line =
    [
        0, 0, 33, 0, 0, 0, 33, 0, 0, 0, 33, 0, 0, 33, 0, 36,
        0, 0, 33, 0, 0, 0, 45, 0, 0, 0, 33, 0, 0, 43, 0, 40,
    ];

    /// <summary>Two bars of the stab, in eighths.</summary>
    private static readonly int[] Stabs = [0, 1, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 1, 0, 1];

    /// <summary>
    /// The hook, four bars of eighths, and two variations on it: one climbing and answering
    /// from the top, one sparse and syncopated. Each ends on B, leading back to A.
    /// </summary>
    private static readonly int[][] Hooks =
    [
        [
            69, 0, 72, 0, 76, 0, 74, 72, 0, 72, 0, 69, 0, 67, 69, 0,
            69, 0, 72, 0, 79, 0, 76, 74, 0, 76, 0, 74, 0, 72, 71, 0,
        ],
        [
            76, 0, 76, 74, 76, 0, 79, 0, 76, 0, 74, 0, 72, 0, 69, 0,
            79, 0, 79, 76, 74, 0, 72, 0, 0, 71, 0, 72, 0, 74, 71, 0,
        ],
        [
            69, 0, 0, 69, 0, 0, 72, 0, 0, 0, 76, 0, 74, 0, 72, 0,
            69, 0, 0, 69, 0, 0, 74, 0, 0, 0, 76, 0, 74, 72, 71, 0,
        ],
    ];

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);
        var boxed = 0;

        b.Patch.Length = 96 * 240.0 / Bpm;

        var pump = b.Patch.AddControl("Pump", 2f / 3f);
        var echo = b.Patch.AddControl("Echo", 0.5f);
        var swing = b.Patch.AddControl("Swing", 0.15f);

        // Slewed, so a knob turned while it plays glides rather than steps.
        var echoed = b.Add(NodeCatalog.SlewTypeId, (1, -1.52f), (2, -1.52f));
        var swung = b.Add(NodeCatalog.SlewTypeId, (1, -1.52f), (2, -1.52f));
        Knob(echoed, 0, echo, 0f, 0.6f);
        Knob(swung, 0, swing, 0f, 1f);

        // --- the arrangement ---------------------------------------------------

        var time = b.Add(NodeCatalog.TimeTypeId);
        var song = Lane(Phrases, Song);
        var pluck = b.Add(NodeCatalog.SlewTypeId, (1, 0.6f), (2, 0.3f));
        var root = Lane(Chords, [.. Roots.Select(r => (float)r)]);

        var phrase = Formula($"fract(a * {N(Phrases)})", time);
        var building = Formula("step(0.45, a) * (1 - step(0.6, a))", song);
        var breakdown = Formula("1 - step(0.08, a)", song);
        var rise = Formula("a * smoothstep(0.5, 1, b)", building, phrase);

        b.Wire(Lane(Phrases, Plucks), 0, pluck, 0);

        Box("Arrangement");

        // --- the drums ---------------------------------------------------------

        var kick = Drum(Kit.Kick, (DrumModule.DrivePort, 0.4f));
        var clap = Drum(Kit.Clap);
        var closed = DrumModule.Configure(
            b.Add(DrumModule.TypeId, (DrumModule.BpmPort, Bpm), (DrumModule.PanPort, -0.3f)),
            (DrumModule.SoundKey, Kit.ClosedHat), (DrumModule.RhythmKey, Rhythms.Sixteenths));
        var open = Drum(Kit.OpenHat, (DrumModule.DecayPort, 0.3f), (DrumModule.PanPort, 0.3f));
        var cowbell = DrumModule.Configure(
            b.Add(DrumModule.TypeId, (DrumModule.BpmPort, Bpm), (DrumModule.PanPort, 0.5f)),
            (DrumModule.SoundKey, Kit.Cowbell), (DrumModule.RhythmKey, Rhythms.Clave));
        var fill = DrumModule.Configure(
            b.Add(DrumModule.TypeId, (DrumModule.BpmPort, Bpm), (DrumModule.PanPort, 0.2f)),
            (DrumModule.SoundKey, Kit.Tom), (DrumModule.RhythmKey, Rhythms.Tresillo));
        var roll = DrumModule.Configure(
            b.Add(DrumModule.TypeId, (DrumModule.BpmPort, Bpm), (DrumModule.TonePort, 0.7f), (DrumModule.PanPort, -0.15f)),
            (DrumModule.SoundKey, Kit.Snare), (DrumModule.RhythmKey, Rhythms.Sixteenths));

        // The kick sits out a build's last bar, so the drop lands on it; a tom fills every phrase's last bar.
        b.Wire(Formula("step(0.08, a) * (1 - b * step(0.875, c))", song, building, phrase), 0, kick, DrumModule.VelocityPort)
         .Wire(Formula("step(0.25, a) * 0.8", song), 0, clap, DrumModule.VelocityPort)
         .Wire(Formula("0.25 + a * 0.2", song), 0, closed, DrumModule.VelocityPort)
         .Wire(Formula("step(0.45, a) * 0.45", song), 0, open, DrumModule.VelocityPort)
         .Wire(Formula("step(0.45, a) * 0.3", song), 0, cowbell, DrumModule.VelocityPort)
         .Wire(Formula("step(0.875, b) * step(0.25, a) * 0.6", song, phrase), 0, fill, DrumModule.VelocityPort)
         .Wire(Formula("a * 0.7", rise), 0, roll, DrumModule.VelocityPort);

        foreach (var shuffled in new[] { closed, cowbell, fill, roll }) b.Wire(swung, 0, shuffled, DrumModule.SwingPort);

        Box("Drums");

        // --- the bass line -----------------------------------------------------

        var notes = b.Add("seq.notes", (1, Sixteenths), (2, 0.8f));
        StepsExtra.Set(notes, [.. Line.Select(n => new Step(n, 1f, n == 0 ? 0f : 1f))]);

        // A square under a sub, round and warm.
        var bass = SynthModule.Configure(
            b.Add(
                SynthModule.TypeId,
                (SynthModule.VelocityPort, 0.9f),
                (SynthModule.SubPort, 0.5f),
                (SynthModule.AttackPort, -3f),
                (SynthModule.DecayPort, -0.45f),
                (SynthModule.SustainPort, 0.5f),
                (SynthModule.ReleasePort, -1.2f),
                (SynthModule.BrightPort, 0.15f),
                (SynthModule.ResonancePort, 0.1f),
                (SynthModule.DrivePort, 0.45f)),
            (SynthModule.WaveKey, Waves.Square));

        b.Wire(Formula("a + b", notes, root), 0, bass, SynthModule.PitchPort)
         .Wire(notes, 1, bass, SynthModule.GatePort)
         .Wire(pluck, 0, bass, SynthModule.SweepPort);

        Box("Bass");

        // --- the chords and the hook --------------------------------------------

        // In the breakdown the stab's gates lengthen and its notes hold, so it is a pad.
        var stabs = b.Add("seq.values", (1, Sixteenths / 2f));
        StepsExtra.Set(stabs, [.. Stabs.Select(n => new Step(n, 1f, n))]);
        b.Wire(Formula("0.35 + a * 0.55", breakdown), 0, stabs, 2);

        var sustain = Formula("a * 0.6", breakdown);
        var release = Formula("-0.8 + a * 1.1", breakdown);
        float[] pans = [-0.4f, 0f, 0.4f];

        var chords = b.Add(NodeCatalog.DeskTypeId, (11, 1f));
        var stabbing = Formula("max(step(0.7, a), b)", song, breakdown);

        for (var voice = 0; voice < Voices.Length; voice++)
        {
            var pitch = Lane(Chords, [.. Voices[voice].Select(n => (float)n)], "seq.notes");
            var stab = SynthModule.Configure(
                b.Add(
                    SynthModule.TypeId,
                    (SynthModule.DecayPort, -0.7f),
                    (SynthModule.BrightPort, 0.6f),
                    (SynthModule.SweepPort, 0.5f),
                    (SynthModule.PanPort, pans[voice])),
                (SynthModule.WaveKey, Waves.Supersaw));

            b.Wire(pitch, 0, stab, SynthModule.PitchPort)
             .Wire(stabs, 1, stab, SynthModule.GatePort)
             .Wire(sustain, 0, stab, SynthModule.SustainPort)
             .Wire(release, 0, stab, SynthModule.ReleasePort)
             .Wire(stabbing, 0, chords, voice * 3 + 2);
            Channel(chords, voice, stab);
        }

        // Soft in the breakdown, growing through the build after it, full from the second half of
        // the first drop and an octave up in the last; it fades rather than stops when a section drops it.
        var riffs = Hooks.Select(hook =>
        {
            var riff = b.Add("seq.notes", (1, Sixteenths / 2f), (2, 0.6f));
            StepsExtra.Set(riff, [.. hook.Select(n => new Step(n, 1f, n == 0 ? 0f : 1f))]);

            return riff;
        }).ToArray();

        // A coin for each pass of the hook: heads plays it as written, tails flips again for
        // which variation. The pass's gate dips for its last few hundredths, which is a rest in all three.
        var pass = b.Add("seq.values", (1, Bars / 4f), (2, 0.995f), (3, 0f));
        StepsExtra.Set(pass, [new Step(1f)]);
        var written = b.Add("seq.chance", (1, 0.5f), (2, 3f));
        var varied = b.Add("seq.chance", (1, 0.5f), (2, 7f));

        b.Wire(pass, 1, written, 0)
         .Wire(written, 1, varied, 0);

        // The hook as written unless a variation's coin is up: in the gaps between passes, too.
        NodeInstance Played(int output) => Formula(
            "a + (b - c) * d",
            Formula("a + (b - a) * c", new Read(riffs[0], output), new Read(riffs[1], output), varied),
            new Read(riffs[2], output),
            new Read(riffs[0], output),
            new Read(varied, 1));

        var lead = SynthModule.Configure(
            b.Add(
                SynthModule.TypeId,
                (SynthModule.DecayPort, -0.52f),
                (SynthModule.SustainPort, 0.35f),
                (SynthModule.ReleasePort, -0.7f),
                (SynthModule.SweepPort, 0.3f),
                (SynthModule.Lfo1RatePort, 5f),
                (SynthModule.Lfo1DepthPort, 0.15f)),
            (SynthModule.WaveKey, Waves.Organ));

        var hooked = b.Add(NodeCatalog.SlewTypeId, (1, -1.3f), (2, 0.6f));

        b.Wire(Played(0), 0, lead, SynthModule.PitchPort)
         .Wire(Played(1), 0, lead, SynthModule.GatePort)
         .Wire(Formula("step(0.82, a) + b * 0.6 + step(0.52, a) * (1 - step(0.6, a)) * (0.6 + c * 0.4)", song, breakdown, rise), 0, hooked, 0)
         .Wire(hooked, 0, lead, SynthModule.VelocityPort)
         .Wire(Formula("step(0.95, a) + step(0.35, a) * (1 - step(0.45, a))", song), 0, lead, SynthModule.OctavePort)
         .Wire(Formula("0.4 + step(0.82, a) * 0.35", song), 0, lead, SynthModule.BrightPort)
         .Wire(lead, 0, chords, 9);

        // A dotted eighth on the left and an eighth on the right.
        var echoLeft = b.Add("audio.delay", (1, 3f / Sixteenths), (2, 0.35f));
        var echoRight = b.Add("audio.delay", (1, 2f / Sixteenths), (2, 0.35f));

        b.Wire(chords, 0, echoLeft, 0)
         .Wire(chords, 1, echoRight, 0)
         .Wire(echoed, 0, echoLeft, 3)
         .Wire(echoed, 0, echoRight, 3);

        Box("Chords");

        // --- the mix -----------------------------------------------------------

        // The bass and the chords are ducked by the kick; the drums go round them on the bus.
        var music = b.Add(NodeCatalog.DeskTypeId, (5, 0.9f));
        var ducked = b.Add("audio.duck", (6, -0.74f));
        Knob(ducked, 3, pump, 0f, 0.9f);
        var drums = b.Add(NodeCatalog.DeskTypeId);
        var master = b.Add(NodeCatalog.DeskTypeId, (5, 0.3f), (14, 0.8f));

        Channel(music, 0, bass);
        b.Wire(echoLeft, 0, music, 3)
         .Wire(echoRight, 0, music, 4)
         .Wire(Formula("(step(0.2, a) + b) * 0.75", song, breakdown), 0, music, 2)
         .Wire(music, 0, ducked, 0)
         .Wire(music, 1, ducked, 1)
         .Wire(kick, DrumModule.EnvPort, ducked, 2);

        Channel(drums, 0, kick);
        Channel(drums, 1, clap);
        Channel(drums, 2, closed);
        Channel(drums, 3, open);
        Channel(master, 0, ducked);
        Channel(master, 1, cowbell);
        Channel(master, 2, roll);
        Channel(master, 3, fill);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.5f));

        b.Wire(drums, 2, master, 12)
         .Wire(drums, 3, master, 13)
         .Wire(master, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(master, 1, output, NodeCatalog.OutputRightPort);

        Box("Mix");

        // --- the picture -------------------------------------------------------

        // Each phrase has its own fold, bend and turn. The fold's center drifts, the kick punches
        // the zoom, and on every bar the fold turns a notch, eased in on the downbeat.
        var twist = Lane(Phrases, Twists);
        var drifted = b.Add("space.translate");
        var zoomed = b.Add("space.scale");
        var turned = b.Add("space.rotate");
        var folded = b.Add("space.kaleidoscope");
        var fog = b.Add("pattern.clouds", (3, 1.8f));
        var ripple = b.Add("pattern.rings");
        var color = b.Add("color.hsv");
        var trails = b.Add("feedback.trails", (7, 0.9f));

        b.Wire(Formula("sin(a * 0.13) * 0.25", time), 0, drifted, 2)
         .Wire(Formula("sin(a * 0.089 + 1) * 0.18", time), 0, drifted, 3)
         .Wire(drifted, 0, zoomed, 0)
         .Wire(drifted, 1, zoomed, 1)
         .Wire(Formula("1.3 + sin(a * 0.11) * 0.25 - b * c * 0.15", time, new Read(kick, DrumModule.EnvPort), song), 0, zoomed, 2)
         .Wire(zoomed, 0, turned, 0)
         .Wire(zoomed, 1, turned, 1)
         .Wire(
             Formula("a * 0.03 + (floor(b * 8) + smoothstep(0, 0.2, fract(b * 8))) * c", time, phrase, twist),
             0, turned, 2)
         .Wire(turned, 0, folded, 0)
         .Wire(turned, 1, folded, 1)
         .Wire(Lane(Phrases, Folds), 0, folded, 2)
         .Wire(folded, 0, fog, 0)
         .Wire(folded, 1, fog, 1)
         .Wire(Formula("a * 0.08", time), 0, fog, 2)
         .Wire(folded, 0, ripple, 0)
         .Wire(folded, 1, ripple, 1)
         .Wire(Formula("2 + a * 4", song), 0, ripple, 2)
         .Wire(
             Formula($"a * {N(Bars)} + b * c + d * 0.1", time, fog, Lane(Phrases, Bends), new Read(kick, DrumModule.EnvPort)),
             0, ripple, 3);

        // The hue follows the chord, turning to the next over its last half bar, and creeps round the wheel over the song.
        var turn = Formula($"smoothstep(0.75, 1, fract(a * {N(Chords)}))", time);
        var shape = Formula("(a * 0.5 + 0.5) * (0.4 + b)", ripple, fog);

        var chord = Formula("a + (b - a) * c", Lane(Chords, Hues), Lane(Chords, NextHues), turn);

        // Each ring its own shade, and the last drop turned warm.
        var shade = Formula("a + b * 0.15 + c * 0.003 + d * 0.12", chord, fog, time, ripple);

        b.Wire(Formula("a + step(0.95, b) * 0.4", shade, song), 0, color, 0)
         .Wire(Formula("0.85 - a * 0.2", fog), 0, color, 1)
         .Wire(
             Formula("(0.25 + a * 0.45 + b * 0.25) * (0.3 + c * 0.7) + d * 0.1", song, rise, shape, new Read(kick, DrumModule.EnvPort)),
             0, color, 2)
         .Wire(color, 0, trails, 0)
         .Wire(Formula("0.995 - a * 0.012 - b * 0.02", song, rise), 0, trails, 3)
         .Wire(Formula("a * 0.01", twist), 0, trails, 4)
         .Wire(trails, 0, output, NodeCatalog.OutputColorPort);

        Box("Picture");

        return b.Build();

        NodeInstance Lane(float rate, float[] values, string type = "seq.values")
        {
            var lane = b.Add(type, (1, rate));
            StepsExtra.Set(lane, [.. values.Select(v => new Step(v))]);

            return lane;
        }

        NodeInstance Formula(string formula, params Read[] sockets)
        {
            var node = b.Add(NodeCatalog.ExpressionTypeId);
            node.SetState("expression", new JsonObject { ["formula"] = formula });

            for (var socket = 0; socket < sockets.Length; socket++)
                b.Wire(sockets[socket].Node, sockets[socket].Port, node, socket);

            return node;
        }

        NodeInstance Drum(string sound, params (int Port, float Value)[] knobs) =>
            DrumModule.Configure(b.Add(DrumModule.TypeId, [(DrumModule.BpmPort, Bpm), .. knobs]), (DrumModule.SoundKey, sound));

        void Channel(NodeInstance desk, int channel, NodeInstance voice) =>
            b.Wire(voice, 0, desk, channel * 3).Wire(voice, 1, desk, channel * 3 + 1);

        void Box(string name)
        {
            b.Group(name, [.. b.Patch.Nodes.Skip(boxed).Where(n => n.TypeId != NodeCatalog.OutputTypeId)]);
            boxed = b.Patch.Nodes.Count;
        }
    }

    /// <summary>One output of a module, as a formula's socket reads it.</summary>
    private readonly record struct Read(NodeInstance Node, int Port = 0)
    {
        public static implicit operator Read(NodeInstance node) => new(node);
    }

    private static string N(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A socket turned by a panel knob from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static void Knob(NodeInstance node, int port, PatchControl knob, float low, float high)
    {
        var link = new ControlLink(knob.Id, low, high);

        node.InputValues[port] = link.At(knob.Value);
        ControlMap.Link(node, port, link);
    }
}
