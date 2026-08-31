using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="Emitter.Interval"/> — how far the renderer's clock moved since the
/// previous evaluation, which is the sample rate said the other way round.
/// </summary>
/// <remarks>
/// It is measured rather than told: a cell holds the clock as it was and the
/// difference is the interval. That puts the clock itself into a cell, and a
/// cell is bounded to the rails because a patch can draw a wire into one and a
/// loop with a gain above one is easy to draw. A clock is not a signal and no
/// wire reaches it, but it passes those rails simply by the patch being left
/// playing — after sixteen seconds — and clamped there it sticks, handing every
/// module that measures its own rate an interval that grows for the rest of the
/// session. Which is a filter that opens, a phaser that stops sweeping and an
/// envelope that finishes in one sample.
/// <para>
/// So the clock is written by <see cref="OpCode.ClockWrite"/> and not by
/// <see cref="OpCode.UnitWrite"/>. These are what says the two are different.
/// </para>
/// </remarks>
public class IntervalTests
{
    private const int Rate = 192_000;

    /// <summary>A program whose only output is the interval.</summary>
    private static (CompiledPatch Program, int Out) Measuring()
    {
        var em = new Emitter();
        var interval = em.Interval();

        return (new CompiledPatch(em.ToProgram(), em.RegisterCount, interval.Base, 1), interval.Base);
    }

    /// <summary>
    /// Reads the interval at each of a run of evaluations starting at
    /// <paramref name="from"/>, stepping at the rate a renderer would.
    /// </summary>
    private static double[] Measure(double from, int count)
    {
        var (program, output) = Measuring();
        var registers = program.AllocateRegisters();
        var state = new DelayState([], Rate, program.PhaseCount, program.UnitCount);
        var step = 1d / Rate;
        var readings = new double[count];

        for (var i = 0; i < count; i++)
        {
            program.Evaluate(0, 0, from + i * step, registers, default, state);
            readings[i] = registers[output];
        }

        return readings;
    }

    [Fact]
    public void It_measures_the_step_the_renderer_is_taking()
    {
        var readings = Measure(0d, 100);

        // The first has no previous evaluation to have moved from.
        readings[0].ShouldBe(0d, 1e-12);

        foreach (var reading in readings[1..])
            reading.ShouldBe(1d / Rate, 1e-12);
    }

    /// <summary>
    /// The regression. Sixteen is where an ordinary cell's clamp would catch
    /// the clock and hold it, turning the interval into the whole of the time
    /// since — growing without end, and never the step again.
    /// </summary>
    [Theory]
    [InlineData(15.9)]
    [InlineData(16d)]
    [InlineData(16.1)]
    [InlineData(60d)]
    [InlineData(600d)]
    [InlineData(3600d)]
    public void It_still_measures_the_step_however_long_the_clock_has_run(double from)
    {
        // Warmed up from the sample before, so the first reading here has a
        // previous evaluation behind it like every other.
        var readings = Measure(from - 1d / Rate, 64);

        foreach (var reading in readings[1..])
            reading.ShouldBe(1d / Rate, 1e-12, $"{from} seconds in");
    }

    /// <summary>
    /// A cell a patch can reach is still held to the rails, which is what stops a
    /// cycle drawn as wires from running away — the clock being let out of that
    /// bound must not have let anything else out with it.
    /// </summary>
    [Fact]
    public void A_cell_a_patch_can_reach_is_still_bounded()
    {
        var state = new DelayState([], Rate, 0, 1);

        state.WriteUnit(0, 1e9d);
        state.ReadUnit(0).ShouldBe(16d);

        state.WriteUnit(0, -1e9d);
        state.ReadUnit(0).ShouldBe(-16d);

        state.WriteUnit(0, double.NaN);
        state.ReadUnit(0).ShouldBe(0d);
    }

    [Fact]
    public void A_clock_is_kept_whole_and_a_broken_one_is_not()
    {
        var state = new DelayState([], Rate, 0, 1);

        state.WriteClock(0, 3600d);
        state.ReadUnit(0).ShouldBe(3600d);

        state.WriteClock(0, double.NaN);
        state.ReadUnit(0).ShouldBe(0d);
    }
}
