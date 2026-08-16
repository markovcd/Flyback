using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Note module is the one place the catalogue does arithmetic that has a
/// right answer outside the program: a note number is a pitch whether or not
/// anyone renders it. These check the emitted ops against
/// <see cref="Pitch"/> rather than against themselves.
/// </summary>
public class NoteTests
{
    private const string TypeId = "audio.note";

    /// <summary>A3, the note the knob opens on.</summary>
    private const float A3 = 57f;

    /// <summary>
    /// Evaluates one of the module's outputs. The audio sink is the shortest way
    /// to get a scalar out of a compiled patch, so gain is set to 1 and the
    /// result read straight out of the output register.
    /// </summary>
    private static float Output(int port, params (int Port, float Value)[] knobs)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var note = builder.Add(TypeId, 0, 0, knobs);
        var sink = builder.Add(NodeCatalog.AudioOutputTypeId, 0, 0, (2, 1f));
        builder.Wire(note, port, sink, 0);

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0f, 0f, 0f, registers, default);

        return registers[program.OutputBase];
    }

    private static float Hz(params (int Port, float Value)[] knobs) => Output(0, knobs);

    private static float Snapped(params (int Port, float Value)[] knobs) => Output(1, knobs);

    /// <summary>
    /// The same, with glide off. Anything asserting an exact note has to pin it:
    /// at the default a value close enough to the halfway point is legitimately
    /// mid-slide, and that is the point of the knob rather than a rounding error.
    /// </summary>
    private static float HardSnapped(params (int Port, float Value)[] knobs) =>
        Output(1, [.. knobs, (3, 0f)]);

    [Fact]
    public void The_note_on_the_knob_is_the_pitch_that_comes_out()
    {
        // A3 is 220 Hz by definition, being an octave below concert A.
        Hz((0, A3)).ShouldBe(220f, 0.01f);
        Hz((0, 69f)).ShouldBe(Pitch.ConcertPitch, 0.01f);
        Hz((0, 60f)).ShouldBe(261.626f, 0.01f);
    }

    /// <summary>The point of the module: what arrives is pulled onto a whole note.</summary>
    [Theory]
    [InlineData(57f)]
    [InlineData(57.2f)]
    [InlineData(57.49f)]
    [InlineData(56.5f)]
    public void An_incoming_value_snaps_to_the_nearest_whole_note(float value)
    {
        Hz((0, value), (3, 0f)).ShouldBe(220f, 0.01f);
        HardSnapped((0, value)).ShouldBe(A3);
    }

    [Fact]
    public void A_value_past_the_halfway_point_snaps_to_the_next_note_up()
    {
        Snapped((0, 57.5f)).ShouldBe(58f);
        Hz((0, 57.9f)).ShouldBe(Pitch.Frequency(58f), 0.01f);
    }

    /// <summary>
    /// Nothing between the two notes survives, which is what separates this from
    /// a Frequency knob: a smooth sweep leaves as steps.
    /// </summary>
    [Fact]
    public void A_sweep_across_a_note_produces_two_pitches_and_nothing_between()
    {
        var heard = new HashSet<float>();

        for (var i = 0; i <= 40; i++)
            heard.Add(HardSnapped((0, 57f + i / 40f)));

        heard.ShouldBe([57f, 58f], ignoreOrder: true);
    }

    /// <summary>
    /// Glide is the fix for the click, and the click is a discontinuity, so this
    /// is the property that matters: however finely the sweep is sampled, the
    /// pitch never jumps. Sampling at a hundredth of a semitone would still find
    /// a step of a whole one if the snap were hard.
    /// </summary>
    [Fact]
    public void Glide_carries_the_pitch_across_the_change_without_a_jump()
    {
        var biggest = 0f;
        var previous = Snapped((0, 57f));

        for (var i = 1; i <= 400; i++)
        {
            var next = Snapped((0, 57f + i / 400f));
            biggest = MathF.Max(biggest, MathF.Abs(next - previous));
            previous = next;
        }

        // A quarter of the sampling step's share of the ramp, with room to spare:
        // the point is that it is nowhere near the whole semitone a snap jumps.
        biggest.ShouldBeLessThan(0.2f);
    }

    /// <summary>Glide costs the end of the step, and nothing else.</summary>
    [Fact]
    public void Glide_leaves_the_note_exact_for_all_but_the_end_of_its_step()
    {
        // Default glide is a fifth of a step, so the other four fifths are dead
        // on the note.
        Snapped((0, 57f)).ShouldBe(A3);
        Snapped((0, 57.2f)).ShouldBe(A3);
        Snapped((0, 56.6f)).ShouldBe(A3);

        // ...and only inside that last fifth is it on its way to the next.
        Snapped((0, 57.49f)).ShouldBeGreaterThan(A3);
        Snapped((0, 57.49f)).ShouldBeLessThan(58f);
    }

    /// <summary>
    /// Turning it off has to give back exactly what a quantiser is: nothing
    /// approximate, and no leftover fraction of a semitone.
    /// </summary>
    [Fact]
    public void Glide_at_zero_is_the_hard_snap_it_always_was()
    {
        for (var i = 0; i <= 40; i++)
            HardSnapped((0, 57f + i / 40f)).ShouldBe(MathF.Floor(57f + i / 40f + 0.5f));
    }

    /// <summary>
    /// Past 1 the ramp would not reach the next note before the step ended, so
    /// the jump would come back — larger than the one it was there to remove.
    /// </summary>
    [Fact]
    public void A_glide_past_the_end_of_its_range_is_held_there()
    {
        for (var i = 0; i <= 40; i++)
        {
            var value = 57f + i / 40f;
            Snapped((0, value), (3, 4f)).ShouldBe(Snapped((0, value), (3, 1f)), 0.0001f);
            Snapped((0, value), (3, -2f)).ShouldBe(HardSnapped((0, value)));
        }
    }

    [Fact]
    public void The_octave_input_moves_by_twelve_semitones()
    {
        Hz((0, A3), (1, 1f)).ShouldBe(440f, 0.01f);
        Hz((0, A3), (1, -1f)).ShouldBe(110f, 0.01f);
        Snapped((0, A3), (1, 2f)).ShouldBe(81f);
    }

    /// <summary>A patched octave is snapped along with the note, not before it.</summary>
    [Fact]
    public void A_fraction_of_an_octave_still_lands_on_a_note()
    {
        // Half an octave is six semitones exactly, so this is a real note...
        Snapped((0, A3), (1, 0.5f)).ShouldBe(63f);

        // ...and a third of one is not, so it snaps.
        Snapped((0, A3), (1, 1f / 3f)).ShouldBe(61f);
    }

    /// <summary>
    /// Detune is the one way to sit between two notes, so it has to apply after
    /// the snap rather than be swallowed by it.
    /// </summary>
    [Fact]
    public void Cents_detune_past_the_snap()
    {
        Hz((0, A3), (2, 100f)).ShouldBe(Pitch.Frequency(58f), 0.01f);
        Hz((0, A3), (2, 50f)).ShouldBe(226.45f, 0.01f);
        Hz((0, A3), (2, -50f)).ShouldBe(213.74f, 0.01f);

        // Detuning does not move the note the module says it is playing.
        Snapped((0, A3), (2, 99f)).ShouldBe(A3);
    }

    /// <summary>
    /// The whole point, end to end: pick a note, feed a sine, listen. Two zero
    /// crossings a cycle, and the first few samples skipped while the DC blocker
    /// settles from a cold start.
    /// </summary>
    [Fact]
    public void A_note_into_a_sine_really_comes_out_at_that_pitch()
    {
        const int sampleRate = AudioRenderer.DefaultSampleRate;

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var time = builder.Add("time", 0, 0, (0, 1f));
        var note = builder.Add(TypeId, 0, 0, (0, A3));
        var osc = builder.Add("osc.sine", 0, 0);
        var sink = builder.Add(NodeCatalog.AudioOutputTypeId, 0, 0, (2, 1f));

        builder.Wire(time, 0, osc, 0)
            .Wire(note, 0, osc, 1)
            .Wire(osc, 0, sink, 0);

        var buffer = new float[sampleRate * 2];
        new AudioRenderer(sampleRate).Render(
            builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program,
            buffer,
            AudioScan.TimeDriven);

        var crossings = 0;
        for (var frame = 20; frame < sampleRate - 1; frame++)
        {
            var a = buffer[frame * 2];
            var b = buffer[(frame + 1) * 2];
            if ((a < 0f && b >= 0f) || (a >= 0f && b < 0f)) crossings++;
        }

        crossings.ShouldBeInRange(438, 441);
    }

    /// <summary>
    /// The Chromatic preset is the module's demonstration, and half of it is
    /// audible. The snapshot test covers what it looks like, so this covers the
    /// half a picture cannot show: that the tone really sits on notes, and
    /// really moves between them in steps.
    /// </summary>
    [Fact]
    public void The_chromatic_preset_plays_one_note_and_then_the_next()
    {
        const int sampleRate = AudioRenderer.DefaultSampleRate;

        var result = Presets.Chromatic(NodeCatalog.BuiltIn).CompileForAudio(NodeCatalog.BuiltIn);
        result.Issues.ShouldBeEmpty();

        var buffer = new float[sampleRate / 2 * 2];
        new AudioRenderer(sampleRate).Render(result.Program, buffer, AudioScan.TimeDriven);

        // The ramp starts at the top of its travel and descends three semitones
        // a second from B4, so the first note lasts a sixth of a second and each
        // one after it a third. The sweep is descending, so each step is entered
        // at its gliding end and settles a fifth of the way in — which is why
        // the second window starts well after the change and the first, which
        // has no change before it, does not have to.
        Heard(0.02f, 0.15f).ShouldBe(Pitch.Frequency(63f), 8f);
        Heard(0.25f, 0.48f).ShouldBe(Pitch.Frequency(62f), 5f);

        // Two zero crossings a cycle, over the left channel.
        float Heard(float from, float to)
        {
            var first = (int)(from * sampleRate);
            var last = (int)(to * sampleRate);
            var crossings = 0;

            for (var frame = first; frame < last; frame++)
            {
                var a = buffer[frame * 2];
                var b = buffer[(frame + 1) * 2];
                if ((a < 0f && b >= 0f) || (a >= 0f && b < 0f)) crossings++;
            }

            return crossings / (2f * (to - from));
        }
    }

    /// <summary>
    /// The click, as something a test can see: a note change used to move the
    /// waveform thirty times further in one sample than the wave itself travels.
    /// </summary>
    /// <remarks>
    /// This only pins the first second, because that is the only part glide can
    /// promise. The pitch an oscillator here actually produces is <c>freq</c>
    /// plus <c>t</c> times how fast freq is moving — phase is 'in' times 'freq'
    /// and nothing accumulates — so the same change measured at twenty seconds
    /// is twenty times the excursion and tears the waveform again whatever the
    /// glide. Removing it there needs an oscillator that carries its phase, and
    /// that needs a stateful op on the audio path, the way ADR-0027 gave delay
    /// lines one. A wider glide is the only lever this module has.
    /// </remarks>
    [Fact]
    public void The_chromatic_preset_does_not_tear_its_waveform_at_a_note_change()
    {
        const int sampleRate = AudioRenderer.DefaultSampleRate;

        var program = Presets.Chromatic(NodeCatalog.BuiltIn).CompileForAudio(NodeCatalog.BuiltIn).Program;

        var buffer = new float[sampleRate * 2];
        new AudioRenderer(sampleRate).Render(program, buffer, AudioScan.TimeDriven);

        // Three note changes land inside this second, at a sixth, a half and
        // five sixths. The first samples are skipped for the DC blocker.
        var steps = new List<float>();
        for (var frame = (int)(0.02 * sampleRate); frame < sampleRate; frame++)
            steps.Add(MathF.Abs(buffer[frame * 2] - buffer[(frame - 1) * 2]));

        // The wave's own sample-to-sample travel is the yardstick: a tear is a
        // step far larger than anything the sine does on its own, so this holds
        // whatever the pitch and the amplitude happen to be. Hard-snapped it is
        // fourteen times the median here, and at a twentieth of this glide — too
        // fast to be gentle, too slow to be instant — it is twenty-seven.
        var typical = steps.Order().ElementAt(steps.Count / 2);
        steps.Max().ShouldBeLessThan(typical * 3f);
    }

    [Theory]
    [InlineData(57f, "A3")]
    [InlineData(60f, "C4")]
    [InlineData(69f, "A4")]
    [InlineData(61f, "C#4")]
    [InlineData(0f, "C-1")]
    [InlineData(127f, "G9")]
    public void A_note_number_is_shown_by_name(float note, string expected) =>
        Pitch.Name(note).ShouldBe(expected);

    /// <summary>
    /// The knob is a float and anything at all can be patched in, so naming has
    /// to be total — including below the bottom of the keyboard, where the
    /// octave numbering runs negative.
    /// </summary>
    [Theory]
    [InlineData(-1f, "B-2")]
    [InlineData(-12f, "C-2")]
    [InlineData(-13f, "B-3")]
    [InlineData(57.4f, "A3")]
    [InlineData(57.6f, "A#3")]
    public void Naming_survives_values_a_keyboard_does_not_have(float note, string expected) =>
        Pitch.Name(note).ShouldBe(expected);

    [Fact]
    public void An_unnameable_value_is_shown_as_a_number() =>
        Pitch.Name(float.NaN).ShouldNotBeNullOrEmpty();

    /// <summary>
    /// The port carries the naming, so the canvas and the inspector cannot drift
    /// apart — and no other port is affected by it.
    /// </summary>
    [Fact]
    public void Only_the_note_input_is_written_out_as_a_note()
    {
        var def = NodeCatalog.BuiltIn.Require(TypeId);

        def.Inputs[0].Format(57f).ShouldBe("A3");
        def.Inputs[1].Format(1.5f).ShouldBe("1.5");
        NodeCatalog.BuiltIn.Require("audio.frequency").Inputs[0].Format(220f).ShouldBe("220");
    }
}
