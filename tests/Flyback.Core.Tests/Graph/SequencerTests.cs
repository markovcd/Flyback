using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The step sequencers. Which step is playing is a function of where the input
/// has got to rather than of what played before, so unlike a delay or an
/// accumulated phase these need no state — and the same run answers for both
/// sinks, because there is only one program.
/// </summary>
/// <remarks>
/// The domain arrives through x rather than through Time, so one compiled
/// program can be swept over a whole pattern without recompiling, and the
/// module's three outputs can be read from a single run.
/// </remarks>
public class SequencerTests
{
    private const string Values = "seq.values";
    private const string Notes = "seq.notes";

    private readonly record struct Reading(float Value, float Gate, float Index);

    /// <summary>
    /// Emits one sequencer straight out of the catalogue and runs it over a
    /// domain, leaving every knob at its default except the ones named.
    /// </summary>
    private static Reading[] Run(string typeId, IEnumerable<float> domain, params (string Port, float Value)[] knobs)
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

        var outputs = def.Emit(emitter, inputs);
        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1);
        var registers = program.AllocateRegisters();
        var readings = new List<Reading>();

        foreach (var x in domain)
        {
            program.Evaluate(x, 0f, 0f, registers, default);

            readings.Add(new Reading(
                registers[outputs[0].Base],
                registers[outputs[1].Base],
                registers[outputs[2].Base]));
        }

        return [.. readings];
    }

    /// <summary>
    /// The middle of step <paramref name="step"/> at the default rate of 4 steps
    /// a second — sampled off the boundaries, where the step being asked about is
    /// unambiguous.
    /// </summary>
    private static float Midpoint(int step) => (step + 0.5f) / 4f;

    private static float[] EveryStepMidpoint(int steps = 8) =>
        [.. Enumerable.Range(0, steps).Select(Midpoint)];

    private static float DefaultOf(string typeId, string port)
    {
        var def = NodeCatalog.BuiltIn.Require(typeId);
        return def.Inputs[Array.FindIndex([.. def.Inputs], p => p.Name == port)].Default;
    }

    [Fact]
    public void Each_step_in_turn_hands_out_the_value_on_its_own_knob()
    {
        var readings = Run(Values, EveryStepMidpoint());

        for (var step = 0; step < 8; step++)
            readings[step].Value.ShouldBe(DefaultOf(Values, $"step {step + 1}"), 1e-5f, $"step {step + 1}");
    }

    [Fact]
    public void The_pattern_starts_again_after_the_last_step()
    {
        var first = Run(Values, EveryStepMidpoint());
        var second = Run(Values, EveryStepMidpoint().Select(x => x + 8f / 4f));

        second.Select(r => r.Value).ShouldBe(first.Select(r => r.Value));
    }

    /// <summary>
    /// Shortening the pattern has to move the wrap, not merely stop reading the
    /// later knobs — otherwise a three-step sequence would sit silent for five.
    /// </summary>
    [Fact]
    public void Fewer_steps_makes_a_shorter_pattern_rather_than_a_gappy_one()
    {
        var readings = Run(Values, EveryStepMidpoint(), ("steps", 3f));

        for (var step = 0; step < 8; step++)
            readings[step].Value.ShouldBe(
                DefaultOf(Values, $"step {step % 3 + 1}"), 1e-5f, $"step {step + 1} of a three-step pattern");
    }

    /// <summary>
    /// A rest is the gate closing, not the value disappearing. Holding the value
    /// through a rest is what lets one sequencer drive a pitch and its own
    /// rhythm at once — and on the screen the colour simply stays put.
    /// </summary>
    [Fact]
    public void A_rest_closes_the_gate_and_leaves_the_value_alone()
    {
        var readings = Run(Values, EveryStepMidpoint(), ("on 3", 0f), ("gate length", 1f));

        readings[2].Gate.ShouldBe(0f);
        readings[2].Value.ShouldBe(DefaultOf(Values, "step 3"), 1e-5f);

        readings[1].Gate.ShouldBe(1f, 1e-5f, "the step before");
        readings[3].Gate.ShouldBe(1f, 1e-5f, "the step after");
    }

    /// <summary>Between the two ends 'on' is a level, which makes it a velocity.</summary>
    [Fact]
    public void A_step_turned_part_way_on_opens_the_gate_part_way()
    {
        var readings = Run(Values, EveryStepMidpoint(), ("on 5", 0.4f), ("gate length", 1f));

        readings[4].Gate.ShouldBe(0.4f, 1e-5f);
    }

    /// <summary>
    /// Without this two identical notes in a row are one note held twice as
    /// long, because nothing else in the patch can see where a step ended.
    /// </summary>
    [Fact]
    public void The_gate_shuts_before_the_step_does()
    {
        // A fifth and four fifths of the way into the first step, either side of
        // a gate that is set to run for half of it.
        var readings = Run(Values, [0.2f / 4f, 0.8f / 4f], ("gate length", 0.5f));

        readings[0].Gate.ShouldBe(1f, 1e-5f, "early in the step");
        readings[1].Gate.ShouldBe(0f, "late in the step");
    }

    [Fact]
    public void The_index_runs_from_nothing_to_just_under_one_across_the_pattern()
    {
        var readings = Run(Values, EveryStepMidpoint());

        for (var step = 0; step < 8; step++)
            readings[step].Index.ShouldBe(step / 8f, 1e-5f, $"step {step + 1}");
    }

    /// <summary>
    /// 'in' is a domain rather than a clock, the rule ADR-0030 set for the
    /// oscillators. A patch is free to drive it with anything at all.
    /// </summary>
    [Fact]
    public void A_domain_running_backwards_runs_the_sequence_backwards()
    {
        var readings = Run(Values, [.. EveryStepMidpoint().Select(x => -x)]);

        // Stepping back from zero lands on the last step of the pattern, then
        // the one before it — the wrap is floored, not truncated towards zero,
        // so there is no double-length step where the sequence crosses nothing.
        readings[0].Value.ShouldBe(DefaultOf(Values, "step 8"), 1e-5f);
        readings[1].Value.ShouldBe(DefaultOf(Values, "step 7"), 1e-5f);
        readings[2].Value.ShouldBe(DefaultOf(Values, "step 6"), 1e-5f);
    }

    [Fact]
    public void A_domain_that_stops_holds_the_step_it_stopped_on()
    {
        var readings = Run(Values, [Midpoint(5), Midpoint(5), Midpoint(5)], ("rate", 4f));

        readings.Select(r => r.Value).Distinct().Count().ShouldBe(1);
        readings[0].Value.ShouldBe(DefaultOf(Values, "step 6"), 1e-5f);
    }

    [Fact]
    public void A_rate_of_zero_holds_the_first_step()
    {
        var readings = Run(Values, [0f, 10f, 1000f], ("rate", 0f));

        foreach (var reading in readings)
            reading.Value.ShouldBe(DefaultOf(Values, "step 1"), 1e-5f);
    }

    /// <summary>
    /// The two modules are one emit function seen twice, so the notes sequencer
    /// has to step exactly as the values one does — only the knobs read
    /// differently.
    /// </summary>
    [Fact]
    public void The_note_sequencer_steps_the_same_way_the_value_one_does()
    {
        var readings = Run(Notes, EveryStepMidpoint());

        for (var step = 0; step < 8; step++)
            readings[step].Value.ShouldBe(DefaultOf(Notes, $"step {step + 1}"), 1e-5f, $"step {step + 1}");
    }

    [Fact]
    public void A_note_sequencers_steps_are_written_out_as_notes()
    {
        var def = NodeCatalog.BuiltIn.Require(Notes);

        foreach (var port in def.Inputs.Where(p => p.Name.StartsWith("step ")))
            port.Display.ShouldBe(PortDisplay.Note, port.Name);

        // ...and its default riff is a tune rather than a row of zeroes.
        Pitch.Name(DefaultOf(Notes, "step 1")).ShouldBe("A3");
    }

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
        var screen = builder.Add(NodeCatalog.VideoOutputTypeId, 0, 0);

        builder.Wire(time, 0, sequence, 0).Wire(sequence, 0, screen, 0);

        var program = builder.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
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
        const int sampleRate = AudioRenderer.DefaultSampleRate;

        var program = SequencePreset().CompileForAudio(NodeCatalog.BuiltIn).Program;
        var buffer = new float[sampleRate * 2];

        new AudioRenderer(sampleRate).Render(program, buffer, AudioScan.TimeDriven);

        // Three steps a second, so a second holds three note starts and three
        // gate closes. The first samples are skipped for the DC blocker and the
        // accumulator's cold start.
        var steps = new List<float>();
        for (var frame = (int)(0.02 * sampleRate); frame < sampleRate; frame++)
            steps.Add(MathF.Abs(buffer[frame * 2] - buffer[(frame - 1) * 2]));

        // The yardstick is the wave's own fastest travel, and it has to be read
        // where the wave is actually sounding: the gate is shut for a third of
        // every step, and those near-silent samples would drag a median down far
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
