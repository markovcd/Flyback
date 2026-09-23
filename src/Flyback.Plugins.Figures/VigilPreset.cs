using System.Text.Json.Nodes;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>
/// Dark ambient on the three Figures: a drone of four voices that is the fog on the
/// screen heard through Overtones, a harmonograph drawing each chord as it swells, and
/// now and then a far bell, a plate struck in a hall.
/// </summary>
/// <remarks>
/// D, E♭, A and A♭ from D phrygian dominant, sixteen seconds a chord, the voices
/// gliding from one to the next. Five turns of the progression make the piece: a turn
/// of bass and wind rising out of nothing, three with the whole drone, the chords, the
/// wind and the rare sounds, and a turn sinking back. Every level that shapes that is a
/// formula of the clock, so the picture keeps the same sections as the sound.
/// <para>
/// The fog is one Clouds field: the screen draws it, and the voices read rows of it as
/// their partials, so the drone's timbre is the fog drifting. Engine modules only
/// besides the three.
/// </para>
/// </remarks>
internal static class VigilPreset
{
    public const string Name = "Vigil";

    /// <summary>Seconds a chord lasts.</summary>
    private const float Chord = 16f;

    /// <summary>The whole piece: five turns of four chords, then it begins again.</summary>
    private const int Song = 320;

    // A chord a step: D, E♭, A (A C E♭) and A♭ (A♭ C E♭). A♭ is outside the scale
    // and borrows its C and E♭. The bass two octaves up is the harmonographs' root.
    private static readonly Step[] Bass = [new(38f), new(39f), new(33f), new(32f)];
    private static readonly Step[] Low = [new(57f), new(58f), new(57f), new(56f)];
    private static readonly Step[] Middle = [new(62f), new(63f), new(63f), new(63f)];
    private static readonly Step[] High = [new(66f), new(67f), new(72f), new(72f)];

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        var fog = b.Patch.AddControl("fog", 0.4f);
        var glow = b.Patch.AddControl("glow", 0.5f);
        var drive = b.Patch.AddControl("drive", 0.4f);
        var twist = b.Patch.AddControl("twist", 0.3f);
        var wind = b.Patch.AddControl("wind", 0.5f);
        var hall = b.Patch.AddControl("hall", 0.6f);

        // --- the sections -------------------------------------------------------

        var time = b.Add(NodeCatalog.TimeTypeId);

        // Seconds into the piece.
        var place = Formula(b, $"a % {Song}", (time, 0));

        // All of it, in from nothing over the first forty seconds and out over the last minute.
        var swell = Formula(b, "smoothstep(0, 40, a) * (1 - smoothstep(262, 316, a))", (place, 0));

        // The middle: the chords, the upper voices, the bells and the knells.
        var lift = Formula(b, "smoothstep(52, 100, a) * (1 - smoothstep(214, 266, a))", (place, 0));

        b.Group("Sections", time, place, swell, lift);

        // --- the fog ------------------------------------------------------------

        // Boiling slowly, and cut to wisps: the screen's mist and each voice's partials.
        var boil = Formula(b, "a * 0.025", (time, 0));
        var clouds = b.Add(NodeCatalog.CloudsTypeId);
        Knob(clouds, 3, fog, 0.8f, 4f);
        var wisps = Formula(b, "smoothstep(0.3, 0.82, a)", (clouds, 0));

        // Where the voices read it: the bass low down, and the chord above it along one
        // row, so the three are one timbre.
        var deep = b.Add(NodeCatalog.SineTypeId, (1, 0.011f), (3, 0.12f), (4, 0.72f));
        var shallow = b.Add(NodeCatalog.SineTypeId, (1, 0.017f), (3, 0.15f), (4, 0.32f));

        b.Wire(boil, 0, clouds, 2);

        b.Group("Fog", boil, clouds, wisps, deep, shallow);

        // --- the drone ----------------------------------------------------------

        var (bass, bassLane) = Voice(b, Bass, "Bass", wisps, deep, cents: 0f, glide: 0.8f, tilt: -3f);
        var (low, _) = Voice(b, Low, "Low", wisps, shallow, cents: 5f, glide: 0.6f, tilt: -6f);
        var (middle, _) = Voice(b, Middle, "Middle", wisps, shallow, cents: -4f, glide: 0.65f, tilt: -6f);
        var (high, _) = Voice(b, High, "High", wisps, shallow, cents: 3f, glide: 0.7f, tilt: -7.5f);

        // The bass is there from the first breath; the chord above it comes up with the middle.
        var bassLevel = Formula(b, "a * 0.9", (swell, 0));
        var upperLevel = Formula(b, "a * (0.12 + 0.4 * b)", (swell, 0), (lift, 0));

        b.Wire(bassLevel, 0, bass, OvertonesModule.AmpPort)
         .Wire(upperLevel, 0, low, OvertonesModule.AmpPort)
         .Wire(upperLevel, 0, middle, OvertonesModule.AmpPort)
         .Wire(upperLevel, 0, high, OvertonesModule.AmpPort);

        // Summed, driven, and closed down to a murmur outside the middle.
        var voices = Formula(b, "a + b + c + d", (bass, OvertonesModule.OutPort), (low, OvertonesModule.OutPort), (middle, OvertonesModule.OutPort), (high, OvertonesModule.OutPort));
        var drives = b.Add(NodeCatalog.DriveTypeId);
        Knob(drives, 1, drive, 1f, 6f);

        var breathe = b.Add(NodeCatalog.SineTypeId, (1, 0.021f));
        var cutoff = Formula(b, "(140 + 2600 * b * (0.15 + 0.85 * a)) * (1 + 0.3 * c)", (lift, 0));
        Knob(cutoff, 1, glow, 0.15f, 1f);
        b.Wire(breathe, 0, cutoff, 2);
        var dark = b.Add(NodeCatalog.FilterTypeId, (2, 0.35f));

        // A slow chorus each side, the two sweeping against each other.
        var sweepLeft = b.Add(NodeCatalog.SineTypeId, (1, 0.07f), (3, 0.006f), (4, 0.022f));
        var sweepRight = b.Add(NodeCatalog.SineTypeId, (1, 0.07f), (2, 0.5f), (3, 0.006f), (4, 0.029f));
        var chorusLeft = b.Add(NodeCatalog.DelayTypeId, (2, 0.35f), (3, 0.5f));
        var chorusRight = b.Add(NodeCatalog.DelayTypeId, (2, 0.35f), (3, 0.5f));

        b.Wire(voices, 0, drives, 0)
         .Wire(drives, 0, dark, 0)
         .Wire(cutoff, 0, dark, 1)
         .Wire(dark, 0, chorusLeft, 0)
         .Wire(dark, 0, chorusRight, 0)
         .Wire(sweepLeft, 0, chorusLeft, 1)
         .Wire(sweepRight, 0, chorusRight, 1);

        b.Group("Drone", bassLevel, upperLevel, voices, drives, breathe, cutoff, dark, sweepLeft, sweepRight, chorusLeft, chorusRight);

        // --- the chords ---------------------------------------------------------

        // A swell on every chord, a major third over the root but a minor one on A.
        var gate = Formula(b, $"step(fract(a / {Chord}), 0.8)", (time, 0));
        var ratio = Formula(b, $"1.25 - 0.05 * step(0.5, fract(a / {4 * Chord})) * step(fract(a / {4 * Chord}), 0.75)", (time, 0));

        var pitch = b.Add("audio.note", (1, 2f));
        var pendulums = b.Add(
            HarmonographModule.TypeId,
            (HarmonographModule.SpeedPort, 0.23f),
            (HarmonographModule.DampingPort, 7f),
            (HarmonographModule.PersistPort, 0.8f),
            (HarmonographModule.SizePort, 1.3f));
        Knob(pendulums, HarmonographModule.TwistPort, twist, 0f, 1f);

        // Three seconds in, two out, so it is silent before the plane lets it go.
        var envelope = b.Add(NodeCatalog.AdsrTypeId, (1, 0.5f), (3, 1f), (4, 0.3f));

        var chordsLeft = Formula(b, "a * b", (pendulums, HarmonographModule.LeftPort), (envelope, 0));
        var chordsRight = Formula(b, "a * b", (pendulums, HarmonographModule.RightPort), (envelope, 0));

        b.Wire(bassLane, 0, pitch, 0)
         .Wire(pitch, 0, pendulums, HarmonographModule.PitchPort)
         .Wire(ratio, 0, pendulums, HarmonographModule.RatioPort)
         .Wire(gate, 0, pendulums, HarmonographModule.TriggerPort)
         .Wire(lift, 0, pendulums, HarmonographModule.VelocityPort)
         .Wire(gate, 0, envelope, 0);

        b.Group("Chords", gate, ratio, pitch, pendulums, envelope, chordsLeft, chordsRight);

        // --- the bell -----------------------------------------------------------

        // Every 110 seconds from 80 in, C6 and ten seconds later D6; the last C, in the
        // fade, goes unanswered.
        var bells = Formula(b, "step((a + 30) % 110, 0.4) + step(10, (a + 30) % 110) * step((a + 30) % 110, 10.4) * step(a, 305)", (place, 0));
        var bellPitch = Formula(b, "1046.5 + 128.16 * step(10, (a + 30) % 110)", (place, 0));

        // A slow rise and fall after each strike, for how long the bell's figure shows.
        var shown = Formula(b, "smoothstep(0, 3, (a + 30) % 10) * (1 - smoothstep(4, 10, (a + 30) % 10)) * step((a + 30) % 110, 20) * (1 - step(305, a) * step(10, (a + 30) % 110))", (place, 0));

        var bell = b.Add(
            PlateModule.TypeId,
            (PlateModule.VelocityPort, 0.8f),
            (PlateModule.AspectPort, 1.37f),
            (PlateModule.DecayPort, 6f),
            (PlateModule.BrightnessPort, 0.7f),
            (PlateModule.StrikeXPort, 0.31f),
            (PlateModule.StrikeYPort, 0.23f));

        // Far off: faint, and mostly its echoes in the hall.
        var far = b.Add(NodeCatalog.DelayTypeId, (1, 0.61f), (2, 0.55f), (3, 0.7f));

        b.Wire(bellPitch, 0, bell, PlateModule.FreqPort)
         .Wire(bells, 0, bell, PlateModule.TriggerPort);

        b.Group("Bell", bells, bellPitch, shown, bell);

        // --- the dark -----------------------------------------------------------

        // A low plate struck three times in the middle, like a door far below.
        var knells = Formula(b, "step((a + 16) % 64, 0.3) * step(100, a) * step(a, 245)", (place, 0));

        var knell = PlateModule.WithModes(b.Add(
            PlateModule.TypeId,
            (PlateModule.FreqPort, 36.7f),
            (PlateModule.AspectPort, 1.9f),
            (PlateModule.DecayPort, 7f),
            (PlateModule.BrightnessPort, 0.9f),
            (PlateModule.StrikeXPort, 0.21f),
            (PlateModule.StrikeYPort, 0.34f)), 2);
        var groan = b.Add(NodeCatalog.DriveTypeId, (1, 3f));

        b.Wire(knells, 0, knell, PlateModule.TriggerPort)
         .Wire(knell, PlateModule.OutPort, groan, 0);

        // A thin voice that drifts in and out of the middle, never on a note.
        var wander = b.Add(NodeCatalog.NoiseTypeId, (1, 0.07f), (2, 5f));
        var breath = b.Add(NodeCatalog.NoiseTypeId, (1, 0.045f), (2, 9f));
        var wailPitch = Formula(b, "900 * exp(a * 0.52)", (wander, 3));
        var wailLevel = Formula(b, "smoothstep(0.25, 0.8, a) * b * 0.07", (breath, 3), (lift, 0));
        var wail = b.Add(NodeCatalog.SineTypeId);

        b.Wire(wailPitch, 0, wail, 1)
         .Wire(wailLevel, 0, wail, 3);

        // Wind, a resonant band over hiss each side, wandering on its own.
        var gustLeft = b.Add(NodeCatalog.NoiseTypeId, (1, 0.06f), (2, 1f));
        var gustRight = b.Add(NodeCatalog.NoiseTypeId, (1, 0.05f), (2, 2f));
        var airLeft = Formula(b, "a * b", (gustLeft, 0), (swell, 0));
        var airRight = Formula(b, "a * b", (gustRight, 0), (swell, 0));
        var howlLeft = Formula(b, "300 * exp(0.83 + a * 0.97)", (gustLeft, 3));
        var howlRight = Formula(b, "300 * exp(0.83 + a * 0.97)", (gustRight, 3));
        var windLeft = b.Add(NodeCatalog.FilterTypeId, (2, 0.72f));
        var windRight = b.Add(NodeCatalog.FilterTypeId, (2, 0.72f));

        b.Wire(airLeft, 0, windLeft, 0)
         .Wire(howlLeft, 0, windLeft, 1)
         .Wire(airRight, 0, windRight, 0)
         .Wire(howlRight, 0, windRight, 1);

        // The bell and the wail share the far echoes.
        var toFar = Formula(b, "a * 0.5 + b", (bell, PlateModule.OutPort), (wail, 0));
        b.Wire(toFar, 0, far, 0);

        b.Group("Sounds", knells, knell, groan, wander, breath, wailPitch, wailLevel, wail, gustLeft, gustRight, airLeft, airRight, howlLeft, howlRight, windLeft, windRight, toFar, far);

        // --- the picture --------------------------------------------------------

        // Indigo mist, black at the start and the end.
        var mist = Formula(b, "a * b * 0.3", (wisps, 0), (swell, 0));
        var dusk = b.Add("color.hsv", (0, 0.69f), (1, 0.65f));

        // The bass's wave laid across it in a dull ember.
        var ember = Formula(b, "a * b * 0.2", (bass, OvertonesModule.WavePort), (swell, 0));
        var embers = b.Add("color.ink", (2, 0.55f), (3, 0.09f), (4, 0.05f));

        // The chords' drawing, in bone.
        var bone = b.Add("color.ink", (1, 0.8f), (2, 0.86f), (3, 0.82f), (4, 0.74f));

        // The bell lights the hall while it rings: cold light where it swings and where its sand is thrown.
        var ringing = Formula(b, "(0.6 * a + 0.35 * (1 - b)) * c", (bell, PlateModule.MotionPort), (bell, PlateModule.FigurePort), (shown, 0));
        var cold = b.Add("color.ink", (2, 0.62f), (3, 0.74f), (4, 0.95f));

        var corners = b.Add("color.vignette", (3, 0.55f), (4, 1.6f), (5, 0.9f));

        b.Wire(mist, 0, dusk, 2)
         .Wire(dusk, 0, embers, 0)
         .Wire(ember, 0, embers, 1)
         .Wire(embers, 0, bone, 0)
         .Wire(pendulums, HarmonographModule.FigurePort, bone, 1)
         .Wire(bone, 0, cold, 0)
         .Wire(ringing, 0, cold, 1)
         .Wire(cold, 0, corners, 0);

        b.Group("Picture", mist, dusk, ember, embers, bone, ringing, cold, corners);

        // --- the sound ----------------------------------------------------------

        // Nearly everything into one long hall; the bell is almost only its reflections.
        var send = Formula(b, "a * 0.3 + b * 0.6 + c + d * 0.7", (dark, 0), (chordsLeft, 0), (far, 0), (groan, 0));
        var reverb = b.Add(NodeCatalog.ReverbTypeId, (1, 0.96f), (2, 0.93f), (3, 1f));

        b.Wire(send, 0, reverb, 0);

        var near = b.Add(NodeCatalog.DeskTypeId, (2, 0.8f), (5, 0.35f), (11, 0.15f));
        Knob(near, 8, wind, 0f, 0.9f);

        var master = b.Add(NodeCatalog.DeskTypeId, (5, 0.08f), (8, 0.35f), (14, 0.9f));
        Knob(master, 2, hall, 0.2f, 1f);

        b.Wire(chorusLeft, 0, near, 0)
         .Wire(chorusRight, 0, near, 1)
         .Wire(chordsLeft, 0, near, 3)
         .Wire(chordsRight, 0, near, 4)
         .Wire(windLeft, 1, near, 6)
         .Wire(windRight, 1, near, 7)
         .Wire(far, 0, near, 9)
         .Wire(reverb, 0, master, 0)
         .Wire(reverb, 1, master, 1)
         .Wire(bell, PlateModule.OutPort, master, 3)
         .Wire(groan, 0, master, 6)
         .Wire(near, 2, master, 12)
         .Wire(near, 3, master, 13);

        b.Group("Mix", send, reverb, near, master);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0.8f));

        b.Wire(corners, 0, output, NodeCatalog.OutputColorPort)
         .Wire(master, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(master, 1, output, NodeCatalog.OutputRightPort);

        b.Patch.Describe(
            "Dark ambient in D phrygian dominant on the three Figures. The drone is four voices reading "
            + "the fog through Overtones, so its timbre is the mist drifting; a harmonograph draws "
            + "the chords D, E♭, A and A♭ as they swell, and a plate far off in the hall is a bell, "
            + "rarely. 'fog' thickens the mist and the timbre with it, 'glow' opens the drone, "
            + "'drive' roughens it, 'twist' bends the drawings, and 'wind' and 'hall' are the mix.");

        return b.Build();
    }

    /// <summary>
    /// One drone voice: a chord lane gliding into eight partials read off a row of the fog.
    /// </summary>
    private static (NodeInstance Voice, NodeInstance Lane) Voice(
        PatchBuilder b, Step[] notes, string name, NodeInstance fog, NodeInstance row,
        float cents, float glide, float tilt)
    {
        var lane = b.Add("seq.notes", (1, 1f / Chord), (2, 1f));
        StepsExtra.Set(lane, notes);

        var pitch = b.Add("audio.note", (2, cents));
        var slide = b.Add(NodeCatalog.SlewTypeId, (1, glide), (2, glide));

        var voice = b.Add(OvertonesModule.TypeId, (OvertonesModule.TiltPort, tilt));

        b.Wire(lane, 0, pitch, 0)
         .Wire(pitch, 0, slide, 0)
         .Wire(slide, 0, voice, OvertonesModule.FreqPort)
         .Wire(row, 0, voice, OvertonesModule.RowPort)
         .Wire(fog, 0, voice, OvertonesModule.SpectrumPort);

        b.Group(name, lane, pitch, slide, voice);

        return (voice, lane);
    }

    /// <summary>An Expression over up to four sockets.</summary>
    private static NodeInstance Formula(PatchBuilder b, string formula, params (NodeInstance Node, int Port)[] sockets)
    {
        var node = b.Add(NodeCatalog.ExpressionTypeId);
        node.SetState("expression", new JsonObject { ["formula"] = formula });

        for (var socket = 0; socket < sockets.Length; socket++)
            b.Wire(sockets[socket].Node, sockets[socket].Port, node, socket);

        return node;
    }

    /// <summary>A socket turned by a panel knob from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static void Knob(NodeInstance node, int port, PatchControl knob, float low, float high)
    {
        var link = new ControlLink(knob.Id, low, high);

        node.InputValues[port] = link.At(knob.Value);
        ControlMap.Link(node, port, link);
    }
}
