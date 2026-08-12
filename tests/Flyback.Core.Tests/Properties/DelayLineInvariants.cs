using CsCheck;
using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// The delay ops are the only ones in the machine that remember anything, so
/// they are the only ones where a bad value does not go away when the knob is
/// turned back. These pin what they do and what they refuse to do.
/// </summary>
/// <remarks>
/// Everything here runs at 1 kHz so a delay in seconds is a whole number of
/// samples: 0.01 s is ten of them. The signal arrives through x, which
/// <see cref="CompiledPatch.Evaluate"/> already takes per evaluation.
/// </remarks>
public class DelayLineInvariants
{
    private const int Rate = 1_000;
    private const float Delay = 0.01f;
    private const int Samples = 10;

    /// <summary>
    /// A line is read before it is written, so the shortest delay it can express
    /// is one evaluation and everything lands one sample later than the nominal
    /// time. Writing first would make a zero-length feedback loop algebraic.
    /// </summary>
    private const int Lag = Samples + 1;

    /// <summary>Runs a one-op program over a signal, and hands back what came out.</summary>
    private static float[] Run(OpCode code, float gain, float[] signal, DelayState? state = null)
    {
        var emitter = new Emitter();

        var result = emitter.DelayLine(
            code,
            emitter.Load(OpCode.LoadX),
            emitter.Constant(gain),
            emitter.Constant(Delay),
            maximum: 0.5f);

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, result.Base, 1);
        var delays = state ?? new DelayState(program.DelayLengths, Rate);
        var registers = program.AllocateRegisters();
        var output = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, 0f, registers, default, delays);
            output[i] = registers[program.OutputBase];
        }

        return output;
    }

    private static float[] Impulse(int length)
    {
        var signal = new float[length];
        signal[0] = 1f;
        return signal;
    }

    [Fact]
    public void A_program_declares_the_lines_it_needs_in_order()
    {
        var emitter = new Emitter();
        var input = emitter.Load(OpCode.LoadX);
        var one = emitter.Constant(0.5f);

        emitter.DelayLine(OpCode.Delay, input, one, one, maximum: 0.25f);
        emitter.DelayLine(OpCode.Allpass, input, one, one, maximum: 0.75f);

        new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, 0, 1)
            .DelayLengths.ShouldBe([0.25f, 0.75f]);
    }

    [Fact]
    public void A_patch_with_no_delays_needs_no_state_at_all()
    {
        var emitter = new Emitter();
        emitter.Binary(OpCode.Add, emitter.Load(OpCode.LoadX), emitter.Constant(1f));

        new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, 0, 1)
            .DelayLengths.ShouldBeEmpty();
    }

    [Fact]
    public void What_goes_in_comes_back_out_a_delay_later()
    {
        var output = Run(OpCode.Delay, 0f, Impulse(64));

        output[Lag].ShouldBe(1f, 1e-4f);

        // And nowhere else: with no feedback it happens exactly once.
        for (var i = 0; i < output.Length; i++)
            if (i != Lag)
                output[i].ShouldBe(0f, 1e-4f);
    }

    [Fact]
    public void Feedback_makes_it_repeat_and_fade()
    {
        var output = Run(OpCode.Delay, 0.5f, Impulse(64));

        output[Lag].ShouldBe(1f, 1e-4f);
        output[Lag * 2].ShouldBe(0.5f, 1e-4f);
        output[Lag * 3].ShouldBe(0.25f, 1e-4f);
        output[Lag * 4].ShouldBe(0.125f, 1e-4f);
    }

    /// <summary>
    /// The failure this guards is the one that persists: at a feedback of one a
    /// line never decays, and above it every pass is louder than the last. Unlike
    /// every other op, turning the knob back down would not undo it.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(4f)]
    [InlineData(-9f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NaN)]
    public void Feedback_at_or_beyond_one_cannot_run_away(float feedback)
    {
        var loud = new float[8_000];
        Array.Fill(loud, 1f);

        var output = Run(OpCode.Delay, feedback, loud);

        foreach (var sample in output)
        {
            float.IsFinite(sample).ShouldBeTrue();
            MathF.Abs(sample).ShouldBeLessThanOrEqualTo(16f);
        }
    }

    /// <summary>
    /// An allpass passes everything through and only rearranges when. The tell is
    /// that an impulse produces something immediately — inverted, scaled by the
    /// gain — rather than only after the delay.
    /// </summary>
    [Fact]
    public void An_allpass_answers_at_once_and_again_later()
    {
        var output = Run(OpCode.Allpass, 0.5f, Impulse(64));

        output[0].ShouldBe(-0.5f, 1e-4f);
        output[Lag].ShouldBe(0.75f, 1e-4f);
    }

    [Theory]
    [InlineData(OpCode.Delay)]
    [InlineData(OpCode.Allpass)]
    public void Neither_op_ever_produces_a_non_finite_value(OpCode code)
    {
        var signal = Gen.Float[-1000f, 1000f].Array[256];

        signal.Sample(samples =>
        {
            foreach (var value in Run(code, 0.9f, samples))
                float.IsFinite(value).ShouldBeTrue($"{code} produced {value}");
        });
    }

    /// <summary>
    /// Without state a delay is a wire. That is what the video path gets: rows
    /// render in parallel, so there is no order for a line to remember, and a
    /// patch written for the speakers still has to draw something.
    /// </summary>
    [Fact]
    public void With_no_state_a_delay_passes_straight_through()
    {
        var emitter = new Emitter();

        var result = emitter.DelayLine(
            OpCode.Delay,
            emitter.Load(OpCode.LoadX),
            emitter.Constant(0.9f),
            emitter.Constant(Delay),
            maximum: 0.5f);

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, result.Base, 1);
        var registers = program.AllocateRegisters();

        foreach (var x in new[] { -1f, -0.25f, 0f, 0.5f, 1f })
        {
            program.Evaluate(x, 0f, 0f, registers, default);
            registers[program.OutputBase].ShouldBe(x);
        }
    }

    [Fact]
    public void Clearing_the_state_silences_what_was_still_ringing()
    {
        var state = new DelayState([0.5f], Rate);

        Run(OpCode.Delay, 0.9f, Impulse(64), state);
        state.Clear();

        Run(OpCode.Delay, 0.9f, new float[64], state)
            .ShouldAllBe(sample => sample == 0f);
    }

    /// <summary>
    /// Which buffer an op uses is its position among the delay ops, so a program
    /// whose delays changed at all must not inherit the old ones.
    /// </summary>
    [Fact]
    public void State_only_fits_a_program_with_the_same_lines()
    {
        var state = new DelayState([0.25f, 0.75f], Rate);

        state.Fits([0.25f, 0.75f], Rate).ShouldBeTrue();
        state.Fits([0.25f], Rate).ShouldBeFalse();
        state.Fits([0.75f, 0.25f], Rate).ShouldBeFalse();
        state.Fits([0.25f, 0.75f], Rate * 2).ShouldBeFalse();
    }
}
