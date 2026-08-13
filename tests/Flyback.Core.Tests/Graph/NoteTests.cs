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
        Hz((0, value)).ShouldBe(220f, 0.01f);
        Snapped((0, value)).ShouldBe(A3);
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
            heard.Add(Snapped((0, 57f + i / 40f)));

        heard.ShouldBe([57f, 58f], ignoreOrder: true);
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

        // The ramp is a quarter of a hertz over twelve semitones from D#3, so
        // the first note lasts a sixth of a second and each one after it a
        // third. Both windows sit inside a note, clear of the boundaries and of
        // the DC blocker settling from a cold start.
        Heard(0.02f, 0.15f).ShouldBe(Pitch.Frequency(51f), 5f);
        Heard(0.19f, 0.48f).ShouldBe(Pitch.Frequency(52f), 3f);

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
