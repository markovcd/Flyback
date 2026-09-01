using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// The text language, against the thing it is a second view of.
/// </summary>
/// <remarks>
/// The load-bearing tests here compare a patch written in the language with the
/// same patch built in C# by <see cref="Presets"/>, and they compare the
/// compiled <em>programs</em> rather than the graphs. That is the equivalence
/// worth holding: two graphs may differ in node order and still be the same
/// instrument, and what a patch is for is what it compiles to.
/// </remarks>
public class LanguageTests
{
    private static Patch Build(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    /// <summary>
    /// The same instrument, whichever way it was written. Both programs, because
    /// a preset built for the ear and one built for the eye each prove only half
    /// of it.
    /// </summary>
    private static void Same(string presetName, string source) => Compare(presetName, source, slack: 0f);

    /// <summary>
    /// The same instrument to within a rounded knob, which is what a preset
    /// authored in decades and a transliteration written in milliseconds can be.
    /// </summary>
    /// <remarks>
    /// A <see cref="PortDisplay.Duration"/> socket holds a power of ten, and the
    /// two presets that reach here were written straight onto that scale:
    /// Waveform's window is -1.7 and Two channels' decay is -0.9. Said as a time
    /// those are 19.95ms and 125.9ms, and nobody writes either — so the
    /// reference says 20ms and 126ms, which is a hundredth of a decade away and
    /// neither audible nor visible.
    /// <para>
    /// Only the constants are loosened. Every opcode, register and operand still
    /// has to match exactly, so this cannot hide a wire in the wrong place — the
    /// mistake the whole suite is for.
    /// </para>
    /// </remarks>
    private static void Alike(string presetName, string source) => Compare(presetName, source, slack: 0.01f);

    private static void Compare(string presetName, string source, float slack)
    {
        var expected = Presets.All.Single(p => p.Name == presetName).Build(NodeCatalog.BuiltIn);
        var actual = Build(source);

        Programs(actual.CompileForVideo(NodeCatalog.BuiltIn).Program,
            expected.CompileForVideo(NodeCatalog.BuiltIn).Program, slack, $"{presetName}: the picture");

        Programs(actual.CompileForAudio(NodeCatalog.BuiltIn).Program,
            expected.CompileForAudio(NodeCatalog.BuiltIn).Program, slack, $"{presetName}: the sound");
    }

    private static void Programs(CompiledPatch actual, CompiledPatch expected, float slack, string what)
    {
        // The shape first, because a wire in the wrong place shows up here and
        // nowhere else — every opcode, register and operand has to match exactly
        // whatever slack a constant is given.
        actual.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C))
            .ShouldBe(expected.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C)), what);

        for (var i = 0; i < expected.Ops.Length; i++)
            actual.Ops[i].K.ShouldBe(expected.Ops[i].K, slack, $"{what}: op {i}");
    }

    // --- the presets, as the reference writes them ----------------------------

    [Fact]
    public void Plasma() => Same("Plasma", """
        let slowly = t * 0.2

        x |> sine(freq: 1.5)
          |> add(y |> sine(freq: 1.1, phase: slowly))
          |> remap(-2..2, 0..1)
          |> hsv(saturation: 0.85, value: 1)
          |> out.color
        """);

    [Fact]
    public void Kaleidoscope() => Same("Kaleidoscope", """
        rotate(angle: t * 0.15)
          |> kaleidoscope(segments: 6)
          |> noise(z: t * 0.3, scale: 2.5)
          |> hsv(saturation: 0.9, value: 1)
          |> out.color
        """);

    [Fact]
    public void Drone() => Same("Drone", """
        let slow = sine(freq: 0.15, amp: 0.5, bias: 0.5)

        sine(freq: frequency(110)) * slow |> out.left

        rings(freq: 3, offset: t)
          |> hsv(hue: slow, saturation: 0.85)
          |> out.color

        out.gain = 0.6
        """);

    [Fact]
    public void Grid() => Same("Grid", """
        let plane = translate(dx: sine(freq: 0.06))
                      |> tile(tiles: 3)
                      |> mirror()
                      |> polar()

        plane |> checker(size: 3)
              |> remap(0..1, 0.14..1)
              |> hsv(hue: plane.angle |> remap(-3.15..3.15, 0..1), saturation: 0.7)
              |> out.color
        """);

    [Fact]
    public void FeedbackTunnel() => Same("Feedback tunnel", """
        let pulse = t * 0.25

        let past = rotate(angle: t * 0.08)
                     |> scale(scale: 1.05)
                     |> feedback()
                     |> gain(gain: 0.95, bias: 0)

        let fresh = rings(freq: 1.5, offset: pulse)
                      |> smoothstep(0.8, 1)
                      |> hsv(hue: pulse, saturation: 1)

        past |> max(fresh) |> out.color
        """);

    [Fact]
    public void Loop() => Same("Loop", """
        let echo = unit()
        let sum  = square(freq: frequency(110)) * 0.06 + echo * 0.94

        echo.in <- sum
        sum |> out.left

        out.gain = 0.5
        """);

    [Fact]
    public void TwoChannels() => Alike("Two channels", """
        let root  = note(A2)
        let twin  = note(root.note, cents: 9)
        let shape = pulse(freq: 1.5, width: 0.3)
                      |> adsr(attack: 10ms, decay: 126ms, sustain: 0.4, release: 158ms)

        saw(freq: root, amp: 0.7) * shape |> out.left
        saw(freq: twin, amp: 0.7) * shape |> out.right

        out.gain = 0.55
        """);

    [Fact]
    public void Waveform() => Alike("Waveform", """
        let voice = saw(freq: frequency(160)) * sine(freq: 0.8, amp: 0.45, bias: 0.55)

        voice |> out.left
        scope(voice, window: 20ms) |> out.color

        out.gain = 0.5
        """);

    [Fact]
    public void Sequence() => Same("Sequence", """
        let steps = notes(rate: 3, gate_length: 0.66) [ A3 C4 D4 E4 G4 E4 D4 C4 ]

        sine(freq: steps |> note()) * steps.gate |> out.left

        rings(freq: steps.index |> remap(0..1, 1.5..9))
          |> remap(-1..1, 0.05..1)
          |> mul(steps.gate |> remap(0..1, 0.4..1))
          |> hsv(hue: steps.index, saturation: 0.8)
          |> out.color

        out.gain = 0.5
        """);

    [Fact]
    public void Heard() => Alike("Heard", """
        let voiced = sine(freq: frequency(70))
                       * (pulse(freq: 2, width: 0.08)
                            |> adsr(attack: 2.5ms, decay: 126ms, sustain: 0, release: 100ms))

        let heard = meter(voiced, window: 32ms)

        voiced |> out.left

        rings(freq: 5)
          |> remap(-1..1, 0.1..1)
          |> mul(heard.peak + 0.18)
          |> hsv(hue: heard, saturation: 0.75)
          |> out.color

        out.gain = 0.6
        """);

    [Fact]
    public void AheadAndBehind() => Alike("Ahead and behind", """
        let tone = saw(freq: sine(freq: 0.4) |> remap(-1..1, 90..320))

        tone |> out.left

        color.mix(scope(tone, window: 25ms), probe(tone, window: 25ms), y |> step())
          |> out.color

        out.gain = 0.45
        """);

    [Fact]
    public void RingScan() => Same("Ring scan", """
        let bands = rings(freq: 4)
        let where = sine(freq: 0.2) |> remap(-1..1, 0.2..0.75)
        let loop  = scan(bands, rate: frequency(110), radius: 0.35, x: where, scale: 1)

        loop |> out.left

        bands |> remap(-1..1, 0.05..0.55)
              |> hsv(hue: where, saturation: 0.7)
              |> add(loop.view)
              |> out.color

        out.gain = 0.45
        """);

    [Fact]
    public void Nebula() => Same("Nebula", """
        let boil  = t * 0.12
        let pulse = t * 0.2

        let folded = rotate(angle: t * 0.05) |> kaleidoscope(segments: 8)
        let field  = folded |> noise(z: boil, scale: 1.4)

        let fresh = folded
                      |> warp(by: field, amount: 0.5)
                      |> rings(freq: 2.5, offset: pulse)
                      |> smoothstep(0.15, 0.85)
                      |> hsv(hue: field + pulse |> fract(), saturation: 0.85)

        let past = scale(scale: 0.99)
                     |> rotate(angle: 0.015)
                     |> feedback()
                     |> gain(gain: 0.92, bias: 0)

        past |> max(fresh) |> out.color
        """);

    [Fact]
    public void Clip() => Same("Clip", """
        sample(level: 0.9, trigger: pulse(freq: 0.5, width: 0.02)) |> out.left

        out.gain = 0.7
        """);

    [Fact]
    public void PictureIn() => Same("Picture in", """
        scale(scale: sine(freq: 0.05) |> remap(-1..1, 0.85..1.4))
          |> rotate(angle: t * 0.05)
          |> warp(by: noise(z: t * 0.15, scale: 1.8), amount: 0.12)
          |> picture()
          |> gain(gain: 1.15, bias: -0.05)
          |> out.color
        """);

    [Fact]
    public void FourVoices() => Same("Four voices", """
        def voice(pitch, bands, hue, rate, phase) = {
          let level = sine(freq: rate, phase: phase, amp: 0.5, bias: 0.5)
          let tone  = sine(freq: note(pitch))
          let tint  = radius |> sine(freq: bands, amp: 0.5, bias: 0.5)
                             |> hsv(hue: hue, saturation: 1)
          (tone, level, tint)
        }

        let (toneA, levelA, tintA) = voice(A2,  1, 0.00, 0.06, 0.00)
        let (toneB, levelB, tintB) = voice(E3,  2, 0.33, 0.09, 0.30)
        let (toneC, levelC, tintC) = voice(A3,  3, 0.58, 0.13, 0.60)
        let (toneD, levelD, tintD) = voice(C#4, 4, 0.83, 0.17, 0.85)

        mixer(toneA, levelA, toneB, levelB, toneC, levelC, toneD, levelD) |> out.left

        mixer(tintA, levelA, tintB, levelB, tintC, levelC, tintD, levelD)
          |> gain(gain: 0.6)
          |> out.color

        out.gain = 0.25
        """);

    [Fact]
    public void InKey() => Alike("In key", """
        let field = noise(z: t * 0.3, scale: 2.2)
        let beat  = pulse(freq: tempo(180), width: 0.12)
        let key   = field |> remap(0..1, 45..69) |> quantiser(hold: beat) [ C D E G A ]

        sine(freq: key |> note())
          * (beat |> adsr(attack: 4ms, decay: 141ms, sustain: 0, release: 32ms))
          |> out.left

        hsv(hue: key * (1 / 12) |> fract() |> remap(0..1, 0.02..0.6),
            saturation: 0.6,
            value: field |> remap(0..1, 0.22..0.95))
          |> out.color

        out.gain = 0.55
        """);

    [Fact]
    public void Played() => Alike("Played", """
        let keys = midi.in(index: 1)

        let width = noise(z: t * 3)
                      |> hold(trigger: keys.trigger)
                      |> remap(0..1, 0.12..0.88)

        let env = keys.gate |> adsr(attack: 4ms, decay: 200ms, sustain: 0.5, release: 100ms)

        pulse(freq: note(keys), width: width) * env |> out.left

        let held = keys |> clamp(36, 84)

        rings(freq: held |> remap(36..84, 2..11))
          |> remap(-1..1, 0.1..1)
          |> mul(env |> remap(0..1, 0.25..1))
          |> hsv(hue: held |> remap(36..84, 0.55..0), saturation: 0.8)
          |> out.color

        out.gain = 0.6
        """);

    /// <summary>
    /// The showcase, and the one that proves the language scales: about a
    /// hundred modules, ten groups, four sequencers, and every group reading
    /// names the one before it made.
    /// </summary>
    [Fact]
    public void WholeBand() => Alike("Whole band", """
        group "Clock" {
          let beat       = tempo(112)
          let eighths    = beat * 2
          let sixteenths = beat * 4
        }

        group "Sequences" {
          let bass = notes(rate: eighths, gate_length: 0.55, shape: 0.02) [
            A1@1.5 A1@0.5%0.55 E2%0.8 A1%0.65
            G1@1.5 G1@0.5%0.55 D2%0.8 G1%0.65
            F1@1.5 C2@0.5%0.6  F1@2%0.85 E1@4
          ]

          let lead = notes(rate: sixteenths, gate_length: 0.62, shape: 0.045) [
            A4 C5%0.8 E5%0.9 C5%0.6   F5 E5%0.85 E5%0 D5%0.9
            B4%0.8 D5%0.7 G5 F5%0.85  E5%0.9 D5%0.6 C5%0.95 C5%0
            B4%0.85 A4 G4%0.7 A4%0.8
          ]

          let kick = values(rate: sixteenths, gate_length: 0.32, shape: 0.01) [
            0 ~ ~ ~  0%0.9 ~ 0%0.5 ~  0%0.95 ~ ~ ~  0%0.85 ~ 0%0.55 ~
          ]

          let hats = values(rate: sixteenths, gate_length: 0.4, shape: 0.01) [
            0.15%0.9 0.1%0.35 0.15%0.6 0.1%0.3  0.15%0.85 0.1%0.35 0.55%0.7 0.1%0.3
            0.15%0.9 0.1%0.35 0.15%0.6 0.1%0.3  0.15%0.8  0.1%0.4  1%0.85   0.1%0.5
          ]
        }

        group "Bass" {
          let tuned  = note(bass)
          let body   = saw(freq: tuned, amp: 0.8) + sine(freq: note(bass, octave: -1), amp: 0.6)
          let shaped = body * (bass.gate |> adsr(attack: 1ms, decay: 79ms,
                                                 sustain: 0.35, release: 63ms))
          let bassOut = shaped * 2.4 |> clamp(-1, 1)
        }

        group "Lead" {
          let tuned  = note(lead)
          let wide   = note(tuned.note, cents: sine(freq: 5.4, amp: 9))
          let swell  = sine(freq: 0.043, amp: 0.5, bias: 0.5)
          let fifth  = triangle(freq: note(lead + 7), amp: 0.5) * swell
          let env    = lead.gate |> adsr(attack: 2ms, decay: 71ms, sustain: 0.28, release: 40ms)

          let voiceL = (saw(freq: tuned, amp: 0.7) + fifth) * env
          let voiceR = (saw(freq: wide,  amp: 0.7) + fifth) * env
        }

        group "Kick" {
          let sweep   = kick.gate |> adsr(attack: 0.5ms, decay: 40ms, sustain: 0, release: 16ms)
          let kickOut = sine(freq: sweep |> remap(0..1, 47..205))
                          * (kick.gate |> adsr(attack: 1.26ms, decay: 240ms,
                                               sustain: 0, release: 79ms))
        }

        group "Hats" {
          let hiss = fract(sin(t * 3571) * 4371.3) |> remap(0..1, -1..1)

          let hatOut = hiss * adsr(gate: hats.gate,
                                   attack: 0.2ms,
                                   decay:  hats |> remap(0..1, -2.5..-0.85),
                                   sustain: 0,
                                   release: 6.3ms)
        }

        group "Desk" {
          mixer(bassOut, 0.55, voiceL, 0.72, kickOut, 1, hatOut, 0.55)
            |> mul(1.2) |> clamp(-1, 1) |> out.left

          mixer(bassOut, 0.55, voiceR, 0.72, kickOut, 1, hatOut, 0.8)
            |> mul(1.2) |> clamp(-1, 1) |> out.right
        }

        group "Picture: Geometry" {
          let boil  = t * 0.18
          let crawl = t * 0.02

          let fold = rotate(angle: t * 0.055 + (bass.index |> remap(0..1, -0.4..0.4)))
                       |> scale(scale: kick.gate |> remap(0..1, 0.96..1.3))
                       |> kaleidoscope(segments: bass.index |> remap(0..1, 3..10))

          let field = fold |> noise(z: boil, scale: 2.1)

          let filament = fold
            |> warp(by: field,
                    amount: sine(freq: 0.071, amp: 0.5, bias: 0.5) |> remap(0..1, 0.2..0.7))
            |> rings(freq: lead.gate |> remap(0..1, 2.6..5.5), offset: t * 0.4)
            |> smoothstep(0.2, 0.95)
        }

        group "Picture: Color" {
          let fresh = hsv(
            hue:        lead.index * 0.8 + field * 0.9 + crawl |> fract(),
            saturation: bass.gate |> remap(0..1, 0.55..0.95),
            value:      filament * (kick.gate |> remap(0..1, 0.75..1.7)) |> clamp(0, 1))
        }

        group "Picture: Feedback" {
          let warm = scale(scale: 1.035)
                       |> rotate(angle: kick.gate |> remap(0..1, 0.012..0.05))
                       |> feedback()
                       |> color.split()

          let cool = scale(scale: 0.972)
                       |> rotate(angle: -0.016)
                       |> feedback()
                       |> color.split()

          rgb(warm, cool.g, cool.b)
            |> gain(gain: 0.85, bias: 0)
            |> max(fresh)
            |> out.color
        }

        out.gain = 0.62
        """);

    // --- the pipe rule ---------------------------------------------------------

    /// <summary>
    /// The rule that earns its keep. Smoothstep is [edge0, edge1, in], so "the
    /// first socket left" would put the signal in an edge — and the patch would
    /// compile, render, and mean something else.
    /// </summary>
    [Fact]
    public void A_pipe_lands_on_in_even_where_in_is_last()
    {
        var patch = Build("rings() |> smoothstep(0.15, 0.85) |> out.color");

        var smoothstep = patch.Nodes.Single(n => n.TypeId == "math.smoothstep");
        var rings = patch.Nodes.Single(n => n.TypeId == "pattern.rings");

        patch.IncomingTo(smoothstep.Id, 2)!.SourceNode.ShouldBe(rings.Id);
        patch.IncomingTo(smoothstep.Id, 0).ShouldBeNull();

        smoothstep.InputValues[0].ShouldBe(0.15f);
        smoothstep.InputValues[1].ShouldBe(0.85f);
    }

    /// <summary>Where a module has no 'in', the tuple fills the leading free sockets — which is what makes geometry chain.</summary>
    [Fact]
    public void A_pair_of_coordinates_fills_x_and_y()
    {
        var patch = Build("rotate(angle: 0.5) |> kaleidoscope(segments: 6) |> out.color");

        var rotate = patch.Nodes.Single(n => n.TypeId == "space.rotate");
        var fold = patch.Nodes.Single(n => n.TypeId == "space.kaleidoscope");

        patch.IncomingTo(fold.Id, 0).ShouldBe(new Connection(rotate.Id, 0, fold.Id, 0));
        patch.IncomingTo(fold.Id, 1).ShouldBe(new Connection(rotate.Id, 1, fold.Id, 1));
    }

    /// <summary>
    /// A socket called 'in' is one signal, so piping into one is a scalar
    /// position and takes the source's first output — which is what makes
    /// 'keys |> clamp(36, 84)' the MIDI In's pitch rather than a complaint about
    /// its other three.
    /// </summary>
    [Fact]
    public void A_pipe_into_in_takes_the_first_output()
    {
        var patch = Build("midi.in() |> clamp(36, 84) |> out.color");

        var keys = patch.Nodes.Single(n => n.TypeId == NodeCatalog.MidiTypeId);
        var clamp = patch.Nodes.Single(n => n.TypeId == "math.clamp");

        patch.IncomingTo(clamp.Id, 0).ShouldBe(new Connection(keys.Id, 0, clamp.Id, 0));
    }

    /// <summary>
    /// A position takes two signals and nothing else does. The Sequence preset
    /// is why: its 'steps |> note()' would otherwise have put the sequencer's
    /// gate into Note's octave and its index into the cents — a patch that
    /// compiles, plays, and is not a tune.
    /// </summary>
    [Fact]
    public void Only_a_position_takes_more_than_one_signal()
    {
        var patch = Build("notes() |> note() |> out.left");

        var steps = patch.Nodes.Single(n => n.TypeId == "seq.notes");
        var note = patch.Nodes.Single(n => n.TypeId == "audio.note");

        patch.IncomingTo(note.Id, 0).ShouldBe(new Connection(steps.Id, 0, note.Id, 0));
        patch.IncomingTo(note.Id, 1).ShouldBeNull();
        patch.IncomingTo(note.Id, 2).ShouldBeNull();
    }

    /// <summary>The pair a Space module hands on, which is what makes geometry chain.</summary>
    [Fact]
    public void A_position_is_carried_on_even_where_the_names_differ()
    {
        // Polar's outputs are radius and angle, and Checker's inputs are x and
        // y. What matches is that both are a position, not what they are called.
        var patch = Build("polar() |> checker(size: 3) |> out.color");

        var polar = patch.Nodes.Single(n => n.TypeId == "space.polar");
        var checker = patch.Nodes.Single(n => n.TypeId == "pattern.checker");

        patch.IncomingTo(checker.Id, 0).ShouldBe(new Connection(polar.Id, 0, checker.Id, 0));
        patch.IncomingTo(checker.Id, 1).ShouldBe(new Connection(polar.Id, 1, checker.Id, 1));
    }

    // --- names -----------------------------------------------------------------

    [Fact]
    public void A_module_is_named_by_the_last_part_of_its_type_id() =>
        Build("kaleidoscope() |> out.color").Nodes
            .ShouldContain(n => n.TypeId == "space.kaleidoscope");

    /// <summary>Two of the ninety collide, and the answer is to say which.</summary>
    [Fact]
    public void An_ambiguous_short_name_names_both_candidates()
    {
        var report = Try("mix(0, 1, 0.5) |> out.color").Report;

        report.ShouldContain("color.mix");
        report.ShouldContain("math.mix");
    }

    [Fact]
    public void A_type_id_written_in_full_settles_it() =>
        Build("math.mix(0, 1, 0.5) |> out.color").Nodes.ShouldContain(n => n.TypeId == "math.mix");

    [Fact]
    public void A_module_that_does_not_exist_is_offered_the_nearest_one() =>
        Try("kaleidoscop() |> out.color").Report.ShouldContain("kaleidoscope");

    [Fact]
    public void A_socket_with_a_space_in_its_name_is_written_with_an_underscore() =>
        Build("out.scan_rate = 90").Output.InputValues[NodeCatalog.OutputScanRatePort].ShouldBe(90f);

    // --- sugar ------------------------------------------------------------------

    /// <summary>One clock however often it is written, which is what every preset does by hand.</summary>
    [Fact]
    public void The_clock_is_one_node_however_often_it_is_read()
    {
        var patch = Build("rings(freq: t * 2, offset: t * 3) |> out.color");

        patch.Nodes.Count(n => n.TypeId == NodeCatalog.TimeTypeId).ShouldBe(1);
    }

    [Fact]
    public void A_note_is_a_number_on_a_note_socket() =>
        Build("note(A3) |> out.left").Nodes
            .Single(n => n.TypeId == "audio.note").InputValues[0].ShouldBe(57f);

    /// <summary>A Duration socket holds the power of ten, which is the trap the literal exists to remove.</summary>
    [Fact]
    public void A_duration_is_written_as_a_time_and_stored_as_a_decade()
    {
        var patch = Build("adsr(attack: 10ms, decay: 100ms, sustain: 0.5, release: 1s) |> out.left");
        var adsr = patch.Nodes.Single(n => n.TypeId == NodeCatalog.AdsrTypeId);

        adsr.InputValues[1].ShouldBe(-2f, 0.0001f);
        adsr.InputValues[2].ShouldBe(-1f, 0.0001f);
        adsr.InputValues[4].ShouldBe(0f, 0.0001f);
    }

    [Fact]
    public void A_duration_on_a_socket_that_is_not_one_is_refused() =>
        Try("sine(freq: 20ms) |> out.left").Report.ShouldContain("not read as a length of time");

    [Fact]
    public void A_note_on_a_socket_that_is_not_one_is_refused() =>
        Try("sine(freq: A3) |> out.left").Report.ShouldContain("not read as a note");

    /// <summary>Two literals are a knob that has already been worked out, not a Divide.</summary>
    [Fact]
    public void Arithmetic_between_two_numbers_is_folded()
    {
        var patch = Build("value(1 / 12) |> out.color");

        patch.Nodes.ShouldNotContain(n => n.TypeId == "math.div");
        patch.Nodes.Single(n => n.TypeId == "value").InputValues[0].ShouldBe(1f / 12f, 0.0001f);
    }

    [Fact]
    public void Arithmetic_involving_a_signal_is_a_module() =>
        Build("value(0.5) * t |> out.color").Nodes.ShouldContain(n => n.TypeId == "math.mul");

    [Fact]
    public void A_pipe_is_looser_than_a_multiply()
    {
        // t * 0.2 |> sine() is (t * 0.2) |> sine(): one Multiply, fed by Time,
        // feeding the oscillator's domain.
        var patch = Build("t * 0.2 |> sine() |> out.left");

        var mul = patch.Nodes.Single(n => n.TypeId == "math.mul");
        var sine = patch.Nodes.Single(n => n.TypeId == "osc.sine");

        patch.IncomingTo(sine.Id, 0)!.SourceNode.ShouldBe(mul.Id);
    }

    // --- the Output --------------------------------------------------------------

    [Fact]
    public void Every_patch_has_its_output_already() =>
        Build(string.Empty).Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);

    [Fact]
    public void A_second_output_is_refused() =>
        Try("output() |> out.color").Report.ShouldContain("already has its Output");

    // --- what a module carries -----------------------------------------------------

    [Fact]
    public void A_step_block_becomes_the_notes_on_the_node()
    {
        var patch = Build("notes(rate: 3) [ A3 C4 D4 E4 ] |> out.left");
        var steps = StepsExtra.Of(patch.Nodes.Single(n => n.TypeId == "seq.notes"));

        steps.Select(s => s.Value).ShouldBe([57f, 60f, 62f, 64f]);
        steps.Select(s => s.Length).ShouldBe([1f, 1f, 1f, 1f]);
    }

    [Fact]
    public void A_rest_is_a_step_with_no_volume() =>
        StepsExtra.Of(Build("notes() [ A3 ~ ] |> out.left").Nodes.Single(n => n.TypeId == "seq.notes"))
            .Select(s => s.Volume).ShouldBe([1f, 0f]);

    /// <summary>
    /// A rest and a silenced note sound alike and are not the same step. The
    /// second keeps its pitch, which is what a phrase does when it holds a note
    /// through a gap — and it is what Whole band's lead is written with.
    /// </summary>
    [Fact]
    public void A_rest_and_a_silenced_note_are_different_steps()
    {
        var rest = StepsExtra.Of(
            Build("notes() [ ~ ] |> out.left").Nodes.Single(n => n.TypeId == "seq.notes"))[0];

        var silenced = StepsExtra.Of(
            Build("notes() [ E5%0 ] |> out.left").Nodes.Single(n => n.TypeId == "seq.notes"))[0];

        rest.Volume.ShouldBe(0f);
        silenced.Volume.ShouldBe(0f);

        rest.Value.ShouldBe(0f);
        silenced.Value.ShouldBe(76f);
    }

    [Fact]
    public void A_subdivision_shares_out_one_step()
    {
        var steps = StepsExtra.Of(
            Build("notes() [ A3 [C4 E4] ] |> out.left").Nodes.Single(n => n.TypeId == "seq.notes"));

        steps.Select(s => s.Value).ShouldBe([57f, 60f, 64f]);
        steps.Select(s => s.Length).ShouldBe([1f, 0.5f, 0.5f]);
    }

    [Fact]
    public void Elongation_makes_one_longer_step() =>
        StepsExtra.Of(Build("notes() [ A3@3 C4 ] |> out.left").Nodes.Single(n => n.TypeId == "seq.notes"))
            .Select(s => s.Length).ShouldBe([3f, 1f]);

    /// <summary>Repetition keeps every step the same length, so the module stays on its cheap path.</summary>
    [Fact]
    public void Repetition_makes_several_even_steps()
    {
        var steps = StepsExtra.Of(
            Build("notes() [ A3!3 ] |> out.left").Nodes.Single(n => n.TypeId == "seq.notes"));

        steps.Count.ShouldBe(3);
        steps.Select(s => s.Value).ShouldAllBe(v => v == 57f);
        steps.Select(s => s.Length).ShouldAllBe(l => l == 1f);
    }

    /// <summary>Eight steps, three of them sounding, spread as evenly as eight allows.</summary>
    [Fact]
    public void A_euclidean_pattern_spreads_what_sounds()
    {
        var steps = StepsExtra.Of(
            Build("values() [ 1(3,8) ] |> out.left").Nodes.Single(n => n.TypeId == "seq.values"));

        steps.Count.ShouldBe(8);
        steps.Count(s => s.Volume > 0f).ShouldBe(3);
    }

    /// <summary>Alternation is unrolled rather than scheduled, and the rate is halved to pay for it.</summary>
    [Fact]
    public void Alternation_doubles_the_list_and_halves_the_rate()
    {
        var patch = Build("notes(rate: 4) [ <A3 C4> E4 ] |> out.left");
        var node = patch.Nodes.Single(n => n.TypeId == "seq.notes");

        StepsExtra.Of(node).Select(s => s.Value).ShouldBe([57f, 64f, 60f, 64f]);
        node.InputValues[1].ShouldBe(2f);
    }

    [Fact]
    public void A_scale_block_becomes_the_pitch_classes() =>
        ScaleExtra.Of(Build("quantiser() [ C D E G A ] |> out.left").Nodes
            .Single(n => n.TypeId == NodeCatalog.QuantiserTypeId)).ShouldBe([0, 2, 4, 7, 9]);

    [Fact]
    public void A_player_names_its_file() =>
        SampleExtra.Of(Build("""sample("kick.wav") |> out.left""").Nodes
            .Single(n => n.TypeId == NodeCatalog.SampleTypeId)).ShouldBe("kick.wav");

    /// <summary>A normalled socket has no knob, and the refusal says what is driving it instead.</summary>
    [Fact]
    public void A_knob_on_a_normalled_socket_is_refused() =>
        Try("sine(in: 0.5) |> out.left").Report.ShouldContain("normalled");

    // --- defs and groups ------------------------------------------------------------

    [Fact]
    public void A_def_is_stamped_out_at_every_call_site()
    {
        var patch = Build("""
            def tone(hz) = sine(freq: frequency(hz), amp: 0.5)

            tone(110) + tone(220) |> out.left
            """);

        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);
        patch.Nodes.Count(n => n.TypeId == "audio.frequency").ShouldBe(2);
    }

    [Fact]
    public void A_def_may_hand_back_several_things()
    {
        var patch = Build("""
            def voice(hz) = {
              let level = sine(freq: 0.1, amp: 0.5, bias: 0.5)
              let tone  = sine(freq: frequency(hz))
              (tone, level)
            }

            let (a, b) = voice(110)

            a * b |> out.left
            """);

        patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);

        // The names are the def's own. A node takes the name it was made under,
        // and reading it again outside as 'a' does not rename it — the label
        // says where the module came from rather than where it was last
        // mentioned.
        patch.Nodes.ShouldContain(n => n.Name == "tone");
        patch.Nodes.ShouldContain(n => n.Name == "level");

        // Both results reach the multiply, which is what taking them apart was for.
        var mul = patch.Nodes.Single(n => n.TypeId == "math.mul");

        patch.IncomingTo(mul.Id, 0).ShouldNotBeNull();
        patch.IncomingTo(mul.Id, 1).ShouldNotBeNull();
    }

    [Fact]
    public void A_def_that_calls_itself_is_refused() =>
        Try("def loop(a) = loop(a)\nloop(1) |> out.color").Report.ShouldContain("calls itself");

    [Fact]
    public void A_group_is_a_box_round_what_was_declared_in_it()
    {
        var patch = Build("""
            group "Voice" {
              let pitch = frequency(110)
              let tone  = sine(freq: pitch)
            }

            tone |> out.left
            """);

        var group = patch.Groups.ShouldHaveSingleItem();

        group.Name.ShouldBe("Voice");
        group.Members.Count.ShouldBe(2);
    }

    // --- naming and layout -------------------------------------------------------------

    /// <summary>A let name becomes the label, so a patch built from text opens already named.</summary>
    [Fact]
    public void A_binding_names_the_node_it_placed() =>
        Build("let carrier = sine(freq: 2) |> out.left").Nodes
            .ShouldContain(n => n.Name == "carrier" && n.TypeId == "osc.sine");

    [Fact]
    public void The_patch_is_laid_out_rather_than_left_at_the_origin() =>
        Build("rings() |> hsv() |> out.color").Nodes
            .Select(n => n.X).Distinct().Count().ShouldBeGreaterThan(1);

    // --- complaints ------------------------------------------------------------------

    [Fact]
    public void A_name_that_was_never_bound_is_reported() =>
        Try("missing |> out.color").Report.ShouldContain("nothing here is called 'missing'");

    [Fact]
    public void Every_mistake_is_reported_rather_than_only_the_first() =>
        Try("nope() |> out.color\nalso() |> out.color").Issues.Count.ShouldBe(2);

    [Fact]
    public void A_complaint_says_where_it_is() =>
        Try("\n\nnope() |> out.color").Issues.ShouldHaveSingleItem().Line.ShouldBe(3);

    [Fact]
    public void Nothing_in_a_source_file_can_throw() =>
        Should.NotThrow(() => PatchLanguage.Build("let ( = |> ) [ \" 3..", NodeCatalog.BuiltIn));
}
