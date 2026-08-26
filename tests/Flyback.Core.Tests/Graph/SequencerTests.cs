using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The step sequencers. Which note is playing is a function of where the input
/// has got to rather than of what played before, so unlike a delay or an
/// accumulated phase these need no state — and the same run answers for both
/// sinks, because there is only one program.
/// </summary>
/// <remarks>
/// The domain arrives through x rather than through Time, so one compiled
/// program can be swept over a whole pattern without recompiling, and the
/// module's three outputs can be read from a single run.
/// <para>
/// The notes are a list on the instance rather than knobs on the module
/// (ADR-0038), so they are handed to the emit directly instead of being set as
/// ports.
/// </para>
/// </remarks>
public class SequencerTests
{
    private const string Values = "seq.values";
    private const string Notes = "seq.notes";

    private readonly record struct Reading(float Value, float Gate, float Index);

    /// <summary>
    /// Emits one sequencer straight out of the catalogue and runs it over a
    /// domain, on its own default notes unless another list is given and with
    /// every knob at its default except the ones named.
    /// </summary>
    private static Reading[] Run(
        string typeId,
        IEnumerable<float> domain,
        IReadOnlyList<Step>? notes = null,
        params (string Port, float Value)[] knobs)
    {
        var (program, outputs) = Compile(typeId, notes, knobs);
        var registers = program.AllocateRegisters();
        var readings = new List<Reading>();

        foreach (var x in domain)
        {
            program.Evaluate(x, 0f, 0f, registers, default);

            readings.Add(new Reading(
                (float)registers[outputs[0].Base],
                (float)registers[outputs[1].Base],
                (float)registers[outputs[2].Base]));
        }

        return [.. readings];
    }

    private static (CompiledPatch Program, Slot[] Outputs) Compile(
        string typeId,
        IReadOnlyList<Step>? notes = null,
        params (string Port, float Value)[] knobs)
    {
        var def = NodeCatalog.BuiltIn.Require(typeId);
        var emitter = new Emitter();
        var inputs = new Slot[def.Inputs.Count];

        inputs[0] = emitter.Load(OpCode.LoadX);

        for (var port = 1; port < inputs.Length; port++)
        {
            var named = Array.FindIndex(knobs, k => k.Port == def.Inputs[port].Name);
            inputs[port] = emitter.Constant(named < 0 ? def.Inputs[port].Default : knobs[named].Value);
        }

        // Sanitised the way the compiler sanitises them, so these runs and a
        // real patch see the same notes.
        var steps = (notes ?? def.Extra<StepsExtra>()?.Spec.Default ?? []).Select(s => s.Sane()).ToArray();
        var outputs = def.Emit(emitter, new EmitContext(inputs, steps));

        return (new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1), outputs);
    }

    /// <summary>
    /// The middle of note <paramref name="step"/> at the default rate of 4 a
    /// second, for a pattern of notes one step long — sampled off the
    /// boundaries, where the note being asked about is unambiguous.
    /// </summary>
    private static float Midpoint(int step) => (step + 0.5f) / 4f;

    private static float[] EveryStepMidpoint(int steps = 8) =>
        [.. Enumerable.Range(0, steps).Select(Midpoint)];

    private static IReadOnlyList<Step> DefaultsOf(string typeId) => Spec(NodeCatalog.BuiltIn.Require(typeId)).Default;

    /// <summary>The steps a module declares, asserting on the way that it declares any.</summary>
    private static StepSpec Spec(NodeDef def) =>
        def.Extra<StepsExtra>()?.Spec ?? throw new InvalidOperationException($"{def.Name} carries no notes.");

    private static float DefaultOf(string typeId, int step) => DefaultsOf(typeId)[step].Value;

    /// <summary>A run of notes one step long, valued so each is telling apart from the rest.</summary>
    private static Step[] Ramp(int count) =>
        [.. Enumerable.Range(0, count).Select(s => new Step((s + 1) / 100f))];

    [Fact]
    public void Each_note_in_turn_hands_out_its_own_value()
    {
        var readings = Run(Values, EveryStepMidpoint());

        for (var step = 0; step < 8; step++)
            readings[step].Value.ShouldBe(DefaultOf(Values, step), 1e-5f, $"note {step + 1}");
    }

    [Fact]
    public void The_pattern_starts_again_after_the_last_note()
    {
        var first = Run(Values, EveryStepMidpoint());
        var second = Run(Values, EveryStepMidpoint().Select(x => x + 8f / 4f));

        second.Select(r => r.Value).ShouldBe(first.Select(r => r.Value));
    }

    /// <summary>
    /// A shorter list has to move the wrap, not merely stop reading the later
    /// notes — otherwise a three-note sequence would sit silent for five.
    /// </summary>
    [Fact]
    public void A_shorter_list_makes_a_shorter_pattern_rather_than_a_gappy_one()
    {
        var notes = Ramp(3);
        var readings = Run(Values, EveryStepMidpoint(), notes);

        for (var step = 0; step < 8; step++)
            readings[step].Value.ShouldBe(
                notes[step % 3].Value, 1e-5f, $"step {step + 1} of a three-note pattern");
    }

    /// <summary>
    /// A rest is the gate closing, not the value disappearing. Holding the value
    /// through a rest is what lets one sequencer drive a pitch and its own
    /// rhythm at once — and on the screen the color simply stays put.
    /// </summary>
    [Fact]
    public void A_rest_closes_the_gate_and_leaves_the_value_alone()
    {
        var notes = Ramp(8);
        notes[2] = notes[2] with { Volume = 0f };

        var readings = Run(Values, EveryStepMidpoint(), notes, ("gate length", 1f));

        readings[2].Gate.ShouldBe(0f);
        readings[2].Value.ShouldBe(notes[2].Value, 1e-5f);

        readings[1].Gate.ShouldBe(1f, 1e-5f, "the note before");
        readings[3].Gate.ShouldBe(1f, 1e-5f, "the note after");
    }

    /// <summary>Between the two ends volume is a level, which makes it a velocity.</summary>
    [Fact]
    public void A_note_turned_part_way_up_opens_the_gate_part_way()
    {
        var notes = Ramp(8);
        notes[4] = notes[4] with { Volume = 0.4f };

        var readings = Run(Values, EveryStepMidpoint(), notes, ("gate length", 1f));

        readings[4].Gate.ShouldBe(0.4f, 1e-5f);
    }

    /// <summary>
    /// Without this two identical notes in a row are one note held twice as
    /// long, because nothing else in the patch can see where a note ended.
    /// </summary>
    [Fact]
    public void The_gate_shuts_before_the_note_does()
    {
        // A fifth and four fifths of the way into the first note, either side of
        // a gate that is set to run for half of it.
        var readings = Run(Values, [0.2f / 4f, 0.8f / 4f], null, ("gate length", 0.5f));

        readings[0].Gate.ShouldBe(1f, 1e-5f, "early in the note");
        readings[1].Gate.ShouldBe(0f, "late in the note");
    }

    [Fact]
    public void The_index_runs_from_nothing_to_just_under_one_across_the_pattern()
    {
        var readings = Run(Values, EveryStepMidpoint());

        for (var step = 0; step < 8; step++)
            readings[step].Index.ShouldBe(step / 8f, 1e-5f, $"note {step + 1}");
    }

    /// <summary>
    /// 'in' is a domain rather than a clock, the rule ADR-0030 set for the
    /// oscillators. A patch is free to drive it with anything at all.
    /// </summary>
    [Fact]
    public void A_domain_running_backwards_runs_the_sequence_backwards()
    {
        var readings = Run(Values, [.. EveryStepMidpoint().Select(x => -x)]);

        // Stepping back from zero lands on the last note of the pattern, then
        // the one before it — the wrap is floored, not truncated towards zero,
        // so there is no double-length note where the sequence crosses nothing.
        readings[0].Value.ShouldBe(DefaultOf(Values, 7), 1e-5f);
        readings[1].Value.ShouldBe(DefaultOf(Values, 6), 1e-5f);
        readings[2].Value.ShouldBe(DefaultOf(Values, 5), 1e-5f);
    }

    [Fact]
    public void A_domain_that_stops_holds_the_note_it_stopped_on()
    {
        var readings = Run(Values, [Midpoint(5), Midpoint(5), Midpoint(5)], null, ("rate", 4f));

        readings.Select(r => r.Value).Distinct().Count().ShouldBe(1);
        readings[0].Value.ShouldBe(DefaultOf(Values, 5), 1e-5f);
    }

    [Fact]
    public void A_rate_of_zero_holds_the_first_note()
    {
        var readings = Run(Values, [0f, 10f, 1000f], null, ("rate", 0f));

        foreach (var reading in readings)
            reading.Value.ShouldBe(DefaultOf(Values, 0), 1e-5f);
    }

    /// <summary>
    /// The two modules are one emit function seen twice, so the notes sequencer
    /// has to step exactly as the values one does — only the notes read
    /// differently.
    /// </summary>
    [Fact]
    public void The_note_sequencer_steps_the_same_way_the_value_one_does()
    {
        var readings = Run(Notes, EveryStepMidpoint());

        for (var step = 0; step < 8; step++)
            readings[step].Value.ShouldBe(DefaultOf(Notes, step), 1e-5f, $"note {step + 1}");
    }

    [Fact]
    public void A_note_sequencers_notes_are_written_out_as_notes()
    {
        var def = NodeCatalog.BuiltIn.Require(Notes);

        Spec(def).Display.ShouldBe(PortDisplay.Note);
        Spec(def).AsPort.Format(57f).ShouldBe("A3");
        Spec(def).AsPort.Stepped.ShouldBeTrue("a note lands on notes");

        // ...and its default riff is a tune rather than a row of zeroes.
        Pitch.Name(DefaultOf(Notes, 0)).ShouldBe("A3");
    }

    [Fact]
    public void A_value_sequencers_notes_are_written_out_as_numbers()
    {
        var def = NodeCatalog.BuiltIn.Require(Values);

        Spec(def).Display.ShouldBe(PortDisplay.Number);
        Spec(def).AsPort.Format(0.25f).ShouldBe("0.25");
    }

    // --- length -------------------------------------------------------------

    /// <summary>
    /// The whole of what a length is for: a note twice as long occupies twice
    /// as much of the pattern, and everything after it moves along.
    /// </summary>
    [Fact]
    public void A_longer_note_holds_for_as_long_as_it_says()
    {
        // Three notes: the first lasts two steps, the others one each, so the
        // pattern is four steps long and reads 1 1 2 3.
        Step[] notes = [new(0.1f, 2f), new(0.2f), new(0.3f)];

        var readings = Run(Values, EveryStepMidpoint(4), notes);

        readings[0].Value.ShouldBe(0.1f, 1e-5f, "first half of the long note");
        readings[1].Value.ShouldBe(0.1f, 1e-5f, "second half of the long note");
        readings[2].Value.ShouldBe(0.2f, 1e-5f);
        readings[3].Value.ShouldBe(0.3f, 1e-5f);
    }

    [Fact]
    public void A_pattern_of_uneven_notes_wraps_at_their_total()
    {
        Step[] notes = [new(0.1f, 2f), new(0.2f), new(0.3f)];

        var first = Run(Values, EveryStepMidpoint(4), notes);
        var second = Run(Values, EveryStepMidpoint(4).Select(x => x + 4f / 4f), notes);

        second.Select(r => r.Value).ShouldBe(first.Select(r => r.Value));
    }

    /// <summary>
    /// The gate is shaped against how far through *this* note we are, so a long
    /// note opens and closes over its own length rather than over a step's.
    /// Without this a two-step note would sound for half of itself and rest.
    /// </summary>
    [Fact]
    public void The_gate_of_a_long_note_runs_the_length_of_that_note()
    {
        Step[] notes = [new(0.1f, 2f), new(0.2f)];

        // Three quarters of the way through the long note, which is past where
        // a one-step gate would already have shut.
        var readings = Run(Values, [1.5f / 4f], notes, ("gate length", 1f));

        readings[0].Gate.ShouldBe(1f, 1e-3f);
    }

    /// <summary>
    /// Every note the same length is the ordinary case, and the emit takes a
    /// shorter route through it. The two routes have to agree exactly, or the
    /// optimisation is a second implementation rather than a shortcut.
    /// </summary>
    [Fact]
    public void The_even_and_uneven_routes_agree_on_an_even_pattern()
    {
        var domain = Enumerable.Range(0, 64).Select(s => s / 16f).ToArray();

        // Identical patterns, but the second is a hair uneven, so it compiles
        // through the general path while the first takes the short one.
        var even = Ramp(6);
        var nudged = (Step[])even.Clone();
        nudged[5] = nudged[5] with { Length = 1.0000001f };

        var quick = Run(Values, domain, even);
        var general = Run(Values, domain, nudged);

        for (var i = 0; i < domain.Length; i++)
        {
            general[i].Value.ShouldBe(quick[i].Value, 1e-4f, $"value at {domain[i]}");
            general[i].Gate.ShouldBe(quick[i].Gate, 1e-3f, $"gate at {domain[i]}");
        }
    }

    [Fact]
    public void The_even_route_is_the_shorter_one()
    {
        var even = Ramp(8);
        var uneven = (Step[])even.Clone();
        uneven[3] = uneven[3] with { Length = 2f };

        Ops(even).ShouldBeLessThan(Ops(uneven));

        static int Ops(Step[] notes) => Compile(Values, notes).Program.Ops.Length;
    }

    // --- what it costs ------------------------------------------------------

    /// <summary>
    /// The reason the notes are a list and not thirty-two sockets: a pattern
    /// costs what it plays. A four-note sequence used to cost the same as an
    /// eight-note one, and a thirty-two-note one could not be built at all.
    /// </summary>
    [Fact]
    public void The_program_is_no_longer_than_the_pattern_it_plays()
    {
        var lengths = new[] { 1, 4, 8, 16, NodeCatalog.MaxSteps }
            .Select(n => Compile(Values, Ramp(n)).Program.Ops.Length)
            .ToArray();

        for (var i = 1; i < lengths.Length; i++)
            lengths[i].ShouldBeGreaterThan(lengths[i - 1], "more notes should never cost fewer ops");

        lengths[0].ShouldBeLessThan(lengths[^1] / 4, "one note should be a fraction of thirty-two");
    }

    /// <summary>A list can only be empty by way of a file, and it holds still rather than throwing.</summary>
    [Fact]
    public void A_sequence_with_no_notes_holds_still()
    {
        var readings = Run(Values, EveryStepMidpoint(), []);

        foreach (var reading in readings)
        {
            reading.Value.ShouldBe(0f);
            reading.Gate.ShouldBe(0f);
        }
    }

    /// <summary>
    /// A length of nothing has nowhere to sound and would divide the gate by
    /// zero. The compiler holds every note to something playable on the way in,
    /// so the emit never has to defend itself.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    [InlineData(float.NaN)]
    public void A_note_with_no_length_is_held_to_something_playable(float length)
    {
        new Step(0.5f, length).Sane().Length.ShouldBeGreaterThanOrEqualTo(Step.ShortestLength);

        var readings = Run(Values, EveryStepMidpoint(2), [new Step(0.5f, length), new Step(0.75f)]);

        foreach (var reading in readings)
            float.IsFinite(reading.Value).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    public void A_volume_outside_the_range_is_held_inside_it(float given, float expected) =>
        new Step(0f, 1f, given).Sane().Volume.ShouldBe(expected);

    // --- the module in a patch ----------------------------------------------

    /// <summary>
    /// Nothing about a sequencer needs to remember anything, which is what keeps
    /// it out of DelayState and off the list of things a recompile disturbs.
    /// </summary>
    [Fact]
    public void A_sequencer_asks_for_no_state_at_all()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = builder.Add("time", 0, 0);
        var sequence = builder.Add(Values, 0, 0);
        var screen = builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        builder.Wire(time, 0, sequence, 0).Wire(sequence, 0, screen, 0);

        var program = builder.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
    }

    /// <summary>A module placed from the catalogue arrives playing its default tune.</summary>
    [Fact]
    public void A_freshly_placed_sequencer_carries_its_notes()
    {
        var placed = NodeInstance.Create(NodeCatalog.BuiltIn.Require(Notes), 0, 0);

        placed.Steps.ShouldNotBeNull().Count.ShouldBe(8);
        placed.Steps![0].Value.ShouldBe(57f);
        placed.Steps.ShouldAllBe(s => s.Length == 1f && s.Volume == 1f);
    }

    [Fact]
    public void A_module_that_is_not_a_sequencer_carries_none()
    {
        NodeInstance.Create(NodeCatalog.BuiltIn.Require("osc.sine"), 0, 0).Steps.ShouldBeNull();
    }

    private static Patch SequencePreset() =>
        Presets.All.Single(p => p.Name == "Sequence").Build(NodeCatalog.BuiltIn);

    [Fact]
    public void The_sequence_preset_plays_a_tone()
    {
        var program = SequencePreset().CompileForAudio(NodeCatalog.BuiltIn).Program;
        var buffer = new float[8_192 * 2];

        new AudioRenderer().Render(program, buffer, AudioScan.TimeDriven);

        buffer.Any(v => MathF.Abs(v) > 0.01f).ShouldBeTrue("the preset should make a sound");
    }

    /// <summary>
    /// The same measurement the Note module's tests use: a click is the waveform
    /// moving far further in one sample than the wave itself travels. A pitch
    /// that steps is handled by the accumulator (ADR-0030), but an amplitude
    /// that steps is not handled by anything — oversampling band-limits a
    /// discontinuity, it does not remove one.
    /// </summary>
    [Fact]
    public void The_sequence_preset_does_not_click_when_a_note_starts_or_stops()
    {
        var program = SequencePreset().CompileForAudio(NodeCatalog.BuiltIn).Program;
        var buffer = new float[AudioRenderer.DefaultSampleRate * 2];

        new AudioRenderer().Render(program, buffer, AudioScan.TimeDriven);

        NoClicks(buffer, AudioRenderer.DefaultSampleRate);
    }

    /// <summary>
    /// The same measurement pointed at uneven notes, which is where the gate is
    /// derived differently — the general route divides by the note's own length
    /// rather than taking a fraction of a step, and an envelope that did not
    /// reach zero at a boundary would tear there.
    /// </summary>
    [Fact]
    public void A_pattern_of_uneven_notes_does_not_click_either()
    {

        var patch = SequencePreset();
        var sequencer = patch.Nodes.Single(n => n.TypeId == Notes);

        // Long, short, long, short — the boundaries now fall at uneven places,
        // and every one of them is a chance to tear.
        for (var s = 0; s < sequencer.Steps!.Count; s++)
            sequencer.Steps[s] = sequencer.Steps[s] with { Length = s % 2 == 0 ? 1.5f : 0.5f };

        var program = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var buffer = new float[AudioRenderer.DefaultSampleRate * 2];

        new AudioRenderer().Render(program, buffer, AudioScan.TimeDriven);

        NoClicks(buffer, AudioRenderer.DefaultSampleRate);
    }

    private static void NoClicks(float[] buffer, int sampleRate)
    {
        // The first samples are skipped for the DC blocker and the
        // accumulator's cold start.
        var steps = new List<float>();
        for (var frame = (int)(0.02 * sampleRate); frame < sampleRate; frame++)
            steps.Add(MathF.Abs(buffer[frame * 2] - buffer[(frame - 1) * 2]));

        // The yardstick is the wave's own fastest travel, and it has to be read
        // where the wave is actually sounding: the gate is shut for a third of
        // every note, and those near-silent samples would drag a median down far
        // enough to make a clean envelope look like a tear.
        var ordered = steps.Order().ToArray();
        var travel = ordered[(int)(ordered.Length * 0.9)];

        steps.Max().ShouldBeLessThan(travel * 3f,
            $"largest step was {steps.Max() / travel:F1}x the wave's own travel "
            + $"and {steps.Max() / ordered[ordered.Length / 2]:F1}x the median");
    }

    /// <summary>
    /// The whole reason the preset exists: one sequencer, two sinks, and a
    /// picture that moves on the beat rather than through it. Two moments a few
    /// steps apart have to look different, or the eye is being told nothing the
    /// ear is not.
    /// </summary>
    [Fact]
    public void The_sequence_preset_shows_a_different_picture_on_a_different_step()
    {
        var program = SequencePreset().CompileForVideo(NodeCatalog.BuiltIn).Program;

        // The preset steps three times a second, so these are the middle of the
        // first step and the middle of the fourth.
        Frame(0.5f / 3f).ShouldNotBe(Frame(3.5f / 3f));

        byte[] Frame(float time)
        {
            const int width = 64;
            const int height = 36;

            var buffer = new byte[width * 4 * height];
            new SynthRenderer().Render(program, time, width, height, buffer, width * 4);
            return buffer;
        }
    }
}
