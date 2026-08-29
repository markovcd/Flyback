using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The ADSR. Unlike the sequencers beside it this one has a memory, so what it
/// hands out is a function of every evaluation before it rather than of where
/// its input has got to — which means these run it, a step at a time, and read
/// the shape off the run.
/// </summary>
/// <remarks>
/// The gate is fed from x rather than from a knob so that one compiled program
/// can be opened and closed without recompiling, and the state is a
/// <see cref="DelayState"/> handed to every evaluation — the same one the audio
/// path passes and the video path does not.
/// </remarks>
public class TempoTests
{
    /// <summary>
    /// The one module that knows a tempo is written in beats a minute while
    /// everything downstream of it counts in beats a second. Nothing else in the
    /// catalogue would let 120 be typed as 120.
    /// </summary>
    [Theory]
    [InlineData(120f, 2f)]
    [InlineData(60f, 1f)]
    [InlineData(90f, 1.5f)]
    [InlineData(174f, 2.9f)]
    public void A_tempo_in_beats_a_minute_comes_out_in_beats_a_second(float bpm, float expected)
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.TempoTypeId);
        var emitter = new Emitter();

        var outputs = def.Emit(emitter, new EmitContext([emitter.Constant(bpm)]));
        var program = new CompiledPatch(
            emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1);

        var registers = program.AllocateRegisters();
        program.Evaluate(0, 0, 0, registers, default);

        ((float)registers[outputs[0].Base]).ShouldBe(expected, 1e-5f);
    }

    /// <summary>The knob it opens on, which is what the Kick preset is set to.</summary>
    [Fact]
    public void It_opens_at_a_hundred_and_twenty() =>
        NodeCatalog.BuiltIn.Require(NodeCatalog.TempoTypeId).Inputs[0].Default.ShouldBe(120f);
}

public class AdsrTests
{
    private const int Rate = 1000;

    /// <summary>Log of a time in seconds, which is what the three time knobs are marked in.</summary>
    private static float Decades(float seconds) => MathF.Log10(seconds);

    /// <summary>
    /// Runs the envelope over a gate given a step at a time, and hands back what
    /// came out at each one. Every knob is its default unless named.
    /// </summary>
    /// <param name="gate">The gate at each evaluation, one entry per step of the clock.</param>
    /// <param name="memory">
    /// Whether the program is given somewhere to remember, which is the whole
    /// difference between the two sinks. Null runs it as the picture does.
    /// </param>
    /// <param name="knobs"></param>
    private static float[] Run(
        IEnumerable<float> gate,
        bool memory = true,
        params (string Port, float Value)[] knobs)
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.AdsrTypeId);
        var emitter = new Emitter();
        var inputs = new Slot[def.Inputs.Count];

        inputs[0] = emitter.Load(OpCode.LoadX);

        for (var port = 1; port < inputs.Length; port++)
        {
            var named = Array.FindIndex(knobs, k => k.Port == def.Inputs[port].Name);
            inputs[port] = emitter.Constant(named < 0 ? def.Inputs[port].Default : knobs[named].Value);
        }

        var outputs = def.Emit(emitter, new EmitContext(inputs));
        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1);

        var registers = program.AllocateRegisters();
        var state = memory
            ? new DelayState([], Rate, 0, emitter.UnitSlotCount)
            : null;

        var readings = new List<float>();
        var t = 0d;

        foreach (var open in gate)
        {
            program.Evaluate(open, 0f, t, registers, default, state);
            readings.Add((float)registers[outputs[0].Base]);
            t += 1d / Rate;
        }

        return [.. readings];
    }

    /// <summary>A gate held open for one span of evaluations and shut for another.</summary>
    private static float[] Gate(int open, int shut) =>
        [.. Enumerable.Repeat(1f, open), .. Enumerable.Repeat(0f, shut)];

    [Fact]
    public void A_gate_that_never_opens_gives_nothing()
    {
        var readings = Run(Enumerable.Repeat(0f, 200));

        readings.ShouldAllBe(v => v == 0f);
    }

    /// <summary>
    /// The attack is a straight line to one and the knob is how long it takes, so
    /// the peak lands where the knob says and not before.
    /// </summary>
    [Fact]
    public void It_rises_to_one_over_the_attack()
    {
        // A tenth of a second at a thousand a second is a hundred evaluations.
        var readings = Run(Gate(300, 0), knobs: ("attack", Decades(0.1f)));

        readings[50].ShouldBe(0.5f, 0.02f, "halfway up at half the attack");
        readings[100].ShouldBe(1f, 0.02f, "at the top when the attack is done");
    }

    /// <summary>
    /// And then falls to the sustain over the decay, where it stays for as long
    /// as the gate is held — however long that is.
    /// </summary>
    [Fact]
    public void It_falls_to_the_sustain_and_stays_there()
    {
        var readings = Run(
            Gate(1000, 0),
            knobs: [("attack", Decades(0.01f)), ("decay", Decades(0.05f)), ("sustain", 0.4f)]);

        readings[10].ShouldBe(1f, 0.02f, "the attack is over by ten milliseconds");
        readings[60].ShouldBe(0.4f, 0.02f, "and the decay by sixty");
        readings[500].ShouldBe(0.4f, 1e-4f, "held, it sits on the sustain");
        readings[999].ShouldBe(0.4f, 1e-4f, "however long it is held for");
    }

    [Fact]
    public void It_falls_to_nothing_over_the_release_once_the_gate_is_let_go()
    {
        var readings = Run(
            Gate(200, 300),
            knobs:
            [
                ("attack", Decades(0.01f)),
                ("decay", Decades(0.01f)),
                ("sustain", 1f),
                ("release", Decades(0.1f)),
            ]);

        readings[199].ShouldBe(1f, 0.02f, "still held at the last evaluation of the gate");
        readings[250].ShouldBe(0.5f, 0.03f, "halfway down at half the release");
        readings[310].ShouldBe(0f, 0.02f, "and silent once the release is done");
        readings[^1].ShouldBe(0f, 1e-5f, "and stays there");
    }

    /// <summary>
    /// The latch that separates the attack from the decay is cleared by the gate
    /// closing rather than by the level, so a note taken while the last one is
    /// still releasing rises again from wherever that had got to.
    /// </summary>
    [Fact]
    public void A_second_note_retriggers_from_where_the_release_had_got_to()
    {
        var knobs = new[]
        {
            ("attack", Decades(0.05f)),
            ("decay", Decades(0.01f)),
            ("sustain", 1f),
            ("release", Decades(1f)),
        };

        // Held, let go for a moment, then taken again while it is still falling.
        var gate = new List<float>();
        gate.AddRange(Enumerable.Repeat(1f, 100));
        gate.AddRange(Enumerable.Repeat(0f, 100));
        gate.AddRange(Enumerable.Repeat(1f, 200));

        var readings = Run(gate, knobs: knobs);

        var letGo = readings[199];

        letGo.ShouldBeInRange(0.5f, 0.99f, "the release should be part way down");
        readings[201].ShouldBeGreaterThan(letGo, "and the second note rises from there");
        readings[260].ShouldBe(1f, 0.02f, "reaching the top again");
    }

    /// <summary>
    /// The level passes back down through every value it rose through, so an
    /// envelope that decided whether it was still attacking by looking at the
    /// level would flip between the two stages for ever. This is that: once the
    /// peak is reached the level only ever falls while the gate is held.
    /// </summary>
    [Fact]
    public void It_does_not_climb_again_after_the_peak()
    {
        var readings = Run(
            Gate(400, 0),
            knobs: [("attack", Decades(0.02f)), ("decay", Decades(0.2f)), ("sustain", 0f)]);

        // From the second evaluation, because the first is the one that answers
        // "have I a memory?" with no and hands out the gate — see below.
        var envelope = readings[1..];
        var peak = Array.IndexOf(envelope, envelope.Max());

        for (var i = peak + 1; i < envelope.Length; i++)
            envelope[i].ShouldBeLessThanOrEqualTo(
                envelope[i - 1] + 1e-6f, $"evaluation {i + 1} rose again after the peak");
    }

    /// <summary>
    /// The cell behind <c>HasMemory</c> is read before it is written, so one
    /// evaluation of every program answers no — and this module says the gate
    /// there. Pinned rather than tolerated: it is one sample at the very start of
    /// a program and only where the gate is open at it, and the fix would be a
    /// second cell spent on a state the module is never in again.
    /// </summary>
    [Fact]
    public void The_first_evaluation_of_all_is_the_gate()
    {
        var readings = Run(Gate(20, 0), knobs: [("attack", Decades(1f)), ("sustain", 0f)]);

        readings[0].ShouldBe(1f, 1e-5f, "the first evaluation has no memory behind it");
        readings[1].ShouldBeLessThan(0.05f, "and the envelope has taken over by the second");
    }

    [Fact]
    public void It_never_leaves_nothing_to_one()
    {
        var gate = new List<float>();

        for (var note = 0; note < 6; note++)
        {
            gate.AddRange(Enumerable.Repeat(1f, 17));
            gate.AddRange(Enumerable.Repeat(0f, 11));
        }

        var readings = Run(gate, knobs: [("sustain", 1f), ("attack", Decades(0.001f))]);

        readings.ShouldAllBe(v => v >= 0f && v <= 1f);
    }

    /// <summary>
    /// With nothing to remember there is no time for a shape to happen in, so
    /// the envelope becomes a wire and hands the gate straight on.
    /// </summary>
    [Fact]
    public void With_no_memory_it_is_the_gate()
    {
        var readings = Run(Gate(5, 5), memory: false);

        readings[..5].ShouldAllBe(v => Math.Abs(v - 1f) < 1e-5f);
        readings[5..].ShouldAllBe(v => v == 0f);
    }

    /// <summary>
    /// And that holds whatever the sustain is, which is the whole reason it is
    /// not the sustain: every percussive sound sets it to nothing, so an
    /// envelope that drew its sustain would draw a black screen for a drum.
    /// </summary>
    [Fact]
    public void With_no_memory_a_sustain_of_nothing_still_draws()
    {
        var readings = Run(Gate(5, 5), memory: false, knobs: ("sustain", 0f));

        readings[..5].ShouldAllBe(v => Math.Abs(v - 1f) < 1e-5f);
    }

    /// <summary>
    /// A time knob is in decades, and a signal patched into one can ask for a
    /// stage of no length at all. That is an instant stage rather than a division
    /// by nothing.
    /// </summary>
    [Fact]
    public void A_stage_of_no_length_is_instant_rather_than_infinite()
    {
        var readings = Run(
            Gate(20, 20),
            knobs: [("attack", -30f), ("decay", -30f), ("release", -30f), ("sustain", 0.5f)]);

        readings.ShouldAllBe(v => float.IsFinite(v));
        readings[5].ShouldBe(0.5f, 1e-4f, "straight to the sustain");
        readings[25].ShouldBe(0f, 1e-4f, "and straight back to nothing");
    }
}
