using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// <see cref="OpCode.Phase"/> exists for one reason: a frequency that changes
/// must not move the waveform, only bend it. These pin that, and the two things
/// it must not cost — the picture, and what the 'in' socket means.
/// </summary>
/// <remarks>
/// Everything here runs at 1 kHz, and the domain arrives through x the way the
/// delay tests feed their signal, so a "sample" is a millisecond and a frequency
/// in hertz is cycles per thousand steps. Frequency arrives through y, so it can
/// be stepped mid-run without recompiling anything.
/// </remarks>
public class PhaseAccumulatorInvariants
{
    private const int Rate = 1_000;
    private const float Step = 1f / Rate;

    /// <summary>
    /// Runs a single accumulator over a domain and a frequency, one evaluation
    /// per entry, and hands back the phase that came out of each.
    /// </summary>
    /// <param name="offset">Added after the accumulation, so it stays the direct phase offset it reads as.</param>
    /// <param name="state">
    /// Null builds one to fit, which is what a program with no delay lines and
    /// one oscillator gets. Passing one in is how a test spans two runs.
    /// </param>
    /// <param name="domain">Where the oscillator is read across — Time, usually, but the point is that anything may be.</param>
    /// <param name="frequency">Cycles per unit of the domain, as it stands at each step.</param>
    private static float[] Run(
        float[] domain,
        float[] frequency,
        float offset = 0f,
        DelayState? state = null)
    {
        var emitter = new Emitter();

        var result = emitter.Phase(
            emitter.Load(OpCode.LoadX),
            emitter.Load(OpCode.LoadY),
            emitter.Constant(offset));

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, result.Base, 1);
        var memory = state ?? new DelayState(program.DelayLengths, Rate, program.PhaseCount);
        var registers = program.AllocateRegisters();
        var output = new float[domain.Length];

        for (var i = 0; i < domain.Length; i++)
        {
            program.Evaluate(domain[i], frequency[i], 0f, registers, default, memory);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>A domain that advances by one sample an evaluation, as Time does.</summary>
    private static float[] Ticking(int length) =>
        [.. Enumerable.Range(0, length).Select(i => i * Step)];

    private static float[] Constant(int length, float value) =>
        [.. Enumerable.Repeat(value, length)];

    /// <summary>
    /// How far the phase moved, taking the shorter way round: a wrap from just
    /// below one to just above zero is a small step forward, not a large one
    /// back, and only the small reading is the one the ear agrees with.
    /// </summary>
    private static float Travelled(float from, float to)
    {
        var difference = to - from + 0.5f;
        return difference - MathF.Floor(difference) - 0.5f;
    }

    [Fact]
    public void A_program_declares_the_accumulators_it_needs()
    {
        var emitter = new Emitter();
        var input = emitter.Load(OpCode.LoadX);
        var one = emitter.Constant(1f);

        emitter.Phase(input, one, one);
        emitter.Phase(input, one, one);

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, 0, 1);

        program.PhaseCount.ShouldBe(2);

        // Cells are not lines: an accumulator holds one number and needs no
        // buffer, so a patch of nothing but oscillators declares no lengths.
        program.DelayLengths.ShouldBeEmpty();
    }

    /// <summary>
    /// The video path passes no state, and there the op has to be exactly the
    /// multiply it replaced — not approximately, or every approved frame in the
    /// snapshot tests would have to be redrawn for a change the audio path asked
    /// for.
    /// </summary>
    [Fact]
    public void Without_state_the_phase_is_the_multiply_it_replaces()
    {
        var emitter = new Emitter();

        var result = emitter.Phase(
            emitter.Load(OpCode.LoadX),
            emitter.Load(OpCode.LoadY),
            emitter.Constant(0.25f));

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, result.Base, 1);
        var registers = program.AllocateRegisters();

        for (var i = 0; i < 64; i++)
        {
            float x = i * 0.37f, y = i * 0.11f;

            program.Evaluate(x, y, 0f, registers, default);

            registers[program.OutputBase].ShouldBe((double)x * y + 0.25d);
        }
    }

    /// <summary>
    /// Nothing changes for a patch that was already working: at a steady
    /// frequency the running total and the multiply are the same number.
    /// </summary>
    [Fact]
    public void A_steady_frequency_accumulates_the_phase_the_multiply_would_have()
    {
        const float frequency = 3f;
        var domain = Ticking(1_000);

        var phases = Run(domain, Constant(domain.Length, frequency));

        for (var i = 0; i < domain.Length; i++)
        {
            var expected = domain[i] * frequency;
            phases[i].ShouldBe(expected - MathF.Floor(expected), 0.0005f);
        }
    }

    /// <summary>
    /// The whole point. However far the frequency jumps, the phase moves by one
    /// step's worth of the frequency it landed on — so the wave's value carries
    /// straight across the change and only its slope is different afterwards.
    /// </summary>
    [Fact]
    public void A_frequency_that_jumps_moves_the_phase_by_one_step_and_no_more()
    {
        const int length = 2_000;
        const float low = 155.56f;
        const float high = 415.30f;

        var domain = Ticking(length);

        // A new pitch every hundred samples, alternating across a minor sixth —
        // far bigger than the semitone the Note module steps by.
        var frequency = new float[length];
        for (var i = 0; i < length; i++)
            frequency[i] = i / 100 % 2 == 0 ? low : high;

        var phases = Run(domain, frequency);

        // The largest step the highest frequency can take in one sample, with a
        // little room for the arithmetic. A jump in phase rather than in slope
        // would be a step of up to half a turn, which is a thousand times this.
        var ceiling = high * Step * 1.001f;

        for (var i = 1; i < length; i++)
            MathF.Abs(Travelled(phases[i - 1], phases[i])).ShouldBeLessThan(ceiling);
    }

    /// <summary>
    /// A frequency of zero is a held note, not a stopped clock: the phase stays
    /// exactly where the last step left it, so silence has a position to resume
    /// from.
    /// </summary>
    [Fact]
    public void A_frequency_of_zero_holds_the_phase_where_it_stood()
    {
        const int length = 400;
        var frequency = new float[length];
        for (var i = 0; i < length; i++) frequency[i] = i < 200 ? 7f : 0f;

        var phases = Run(Ticking(length), frequency);

        for (var i = 200; i < length; i++) phases[i].ShouldBe(phases[199]);
    }

    /// <summary>
    /// The step is measured from the input, not from a clock the op keeps to
    /// itself. That is what keeps 'in' the socket it has always been — a domain
    /// a patch can drive with any signal it likes, rather than a wire that only
    /// Time may be plugged into.
    /// </summary>
    [Fact]
    public void A_domain_that_stops_moving_stops_the_phase_with_it()
    {
        const int length = 400;
        var domain = new float[length];
        for (var i = 0; i < length; i++) domain[i] = MathF.Min(i, 200) * Step;

        var phases = Run(domain, Constant(length, 7f));

        // The evaluation at 200 is the last one that moved, being the first to
        // see the domain standing still; everything after it repeats that phase.
        for (var i = 201; i < length; i++) phases[i].ShouldBe(phases[200]);

        // ...and a domain running backwards runs the phase backwards, which is
        // what makes a reversed sweep a reversed sound.
        var reversed = Run(
            [.. Enumerable.Range(0, 100).Select(i => -i * Step)],
            Constant(100, 7f));

        for (var i = 1; i < 100; i++) Travelled(reversed[i - 1], reversed[i]).ShouldBeLessThan(0f);
    }

    /// <summary>
    /// The offset is added after the accumulation rather than into it, so it
    /// stays the direct thing a phase knob reads as.
    /// </summary>
    [Fact]
    public void The_offset_shifts_the_phase_by_exactly_itself()
    {
        var domain = Ticking(200);
        var frequency = Constant(domain.Length, 5f);

        var plain = Run(domain, frequency);
        var shifted = Run(domain, frequency, offset: 0.25f);

        for (var i = 0; i < domain.Length; i++)
            shifted[i].ShouldBe(plain[i] + 0.25f, 0.0001f);
    }

    /// <summary>
    /// The accumulator is the one place besides a delay line where a bad value
    /// would persist after the knob that produced it was turned back, so it is
    /// held to the same rule: a non-finite input carries no distance.
    /// </summary>
    [Fact]
    public void A_non_finite_input_leaves_the_phase_untouched_rather_than_poisoning_it()
    {
        var domain = Ticking(300);
        var frequency = Constant(domain.Length, 5f);

        domain[100] = float.NaN;
        frequency[150] = float.PositiveInfinity;
        frequency[151] = float.NaN;

        var phases = Run(domain, frequency);

        foreach (var phase in phases)
        {
            float.IsFinite(phase).ShouldBeTrue();
            phase.ShouldBeInRange(0f, 1f);
        }

        // And it recovers rather than sticking: a bad frequency loses its one
        // step, a bad domain reading loses nothing at all — the next good one is
        // measured against where the input was before it went wrong.
        Travelled(phases[^2], phases[^1]).ShouldBe(5f * Step, 0.0001f);
    }

    /// <summary>
    /// Rewinding the renderer rewinds the phase with it, so a patch played twice
    /// from the start sounds the same both times.
    /// </summary>
    [Fact]
    public void Clearing_the_state_starts_the_phase_over()
    {
        var domain = Ticking(200);
        var frequency = Constant(domain.Length, 5f);

        var state = new DelayState([], Rate, 1);

        var first = Run(domain, frequency, state: state);
        state.Clear();
        var second = Run(domain, frequency, state: state);

        second.ShouldBe(first);
    }

    /// <summary>
    /// State is reused across a recompile only when it still fits, and an
    /// accumulator counts towards that: a program that grew an oscillator would
    /// otherwise index into the cell belonging to a different one.
    /// </summary>
    [Fact]
    public void State_only_fits_a_program_needing_the_same_accumulators()
    {
        var state = new DelayState([], Rate, 2);

        state.Fits([], Rate, 2).ShouldBeTrue();
        state.Fits([], Rate, 1).ShouldBeFalse();
        state.Fits([], Rate, 3).ShouldBeFalse();
        state.PhaseCount.ShouldBe(2);
    }
}
