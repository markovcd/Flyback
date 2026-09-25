using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A Clock In is a MIDI In for the transport: what an instrument's ticks and
/// buttons become on the shell's side, and how a program turns a beat that moves
/// twenty-four times a second into one that moves every evaluation.
/// </summary>
public class MidiClockTests
{
    private const string Box = "midi:box";

    private const int Beats = 0;
    private const int Bpm = 1;
    private const int Running = 2;
    private const int Reset = 3;

    /// <summary>Evaluations a second, chosen so a tick at 125 bpm is a whole number of them.</summary>
    private const double Rate = 1_000d;

    private const double Tick = 60d / 125d / MidiSignal.TicksPerBeat;

    private const int TickSamples = (int)(Tick * Rate);

    private const double BeatsPerSecond = 125d / 60d;

    // ---- the follower ---------------------------------------------------------

    [Fact]
    public void Two_ticks_a_forty_eighth_of_a_second_apart_are_120_bpm()
    {
        var clock = new MidiClock();

        clock.Tick(0d);
        clock.Tick(1d / 48d);

        clock.Bpm.ShouldBe(120f, 0.001f);
    }

    [Fact]
    public void The_first_tick_after_a_start_is_the_beat_itself()
    {
        var clock = new MidiClock();

        clock.Start();
        clock.Beat.ShouldBe(0f);

        clock.Tick(0d);
        clock.Beat.ShouldBe(0f);

        clock.Tick(1d / 48d);
        clock.Beat.ShouldBe(1f / 24f, 1e-6f);

        for (var tick = 2; tick <= MidiSignal.TicksPerBeat; tick++) clock.Tick(tick / 48d);

        clock.Beat.ShouldBe(1f, 1e-6f);
    }

    /// <summary>A stopped drum machine goes on sending its clock, and that is how the tempo is known before Start.</summary>
    [Fact]
    public void Ticks_while_stopped_measure_the_tempo_and_move_nothing()
    {
        var clock = new MidiClock();

        for (var tick = 0; tick < 10; tick++) clock.Tick(tick / 48d);

        clock.Bpm.ShouldBe(120f, 0.001f);
        clock.Beat.ShouldBe(0f);
        clock.Rate.ShouldBe(0f);
        clock.Running.ShouldBeFalse();
    }

    [Fact]
    public void Stopping_holds_the_beat_and_continuing_goes_on_from_it()
    {
        var clock = new MidiClock();

        clock.Start();
        for (var tick = 0; tick < 13; tick++) clock.Tick(tick / 48d);

        clock.Stop();
        clock.Tick(13d / 48d);
        clock.Tick(14d / 48d);

        clock.Beat.ShouldBe(12f / 24f, 1e-6f);
        clock.Rate.ShouldBe(0f);

        clock.Continue();
        clock.Tick(15d / 48d);

        clock.Beat.ShouldBe(13f / 24f, 1e-6f);
        clock.Rate.ShouldBe(2f, 0.001f);
    }

    [Fact]
    public void A_start_begins_again_from_nought_and_is_counted()
    {
        var clock = new MidiClock();

        clock.Start();
        for (var tick = 0; tick < 30; tick++) clock.Tick(tick / 48d);

        clock.Start();

        clock.Beat.ShouldBe(0f);
        clock.Starts.ShouldBe(2f);
    }

    [Fact]
    public void A_song_position_moves_the_beat()
    {
        var clock = new MidiClock();

        clock.Position(16);
        clock.Beat.ShouldBe(4f);

        clock.Continue();
        clock.Tick(0d);
        clock.Tick(1d / 48d);

        clock.Beat.ShouldBe(4f + 1f / 24f, 1e-6f);
    }

    /// <summary>A cable unplugged for a while is not a tempo of one beat a minute.</summary>
    [Fact]
    public void A_long_silence_is_not_a_tempo()
    {
        var clock = new MidiClock();

        clock.Tick(0d);
        clock.Tick(1d / 48d);
        clock.Tick(10d);

        clock.Bpm.ShouldBe(120f, 0.001f);
    }

    // ---- the module -----------------------------------------------------------

    [Fact]
    public void A_patch_holding_one_asks_for_its_instruments_clock()
    {
        Heard(Beats).LiveInputs.ShouldBe(
            [
                MidiSignal.ClockKey(Box, MidiSignal.Beat),
                MidiSignal.ClockKey(Box, MidiSignal.Rate),
                MidiSignal.ClockKey(Box, MidiSignal.Bpm),
                MidiSignal.ClockKey(Box, MidiSignal.Running),
                MidiSignal.ClockKey(Box, MidiSignal.Starts),
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// The whole point of the line: a beat the shell moves forty-eight times a
    /// second comes out moving every evaluation, at the tempo, and passes through
    /// the beat the instrument said when it said it.
    /// </summary>
    [Fact]
    public void Between_ticks_the_beats_run_on_at_the_tempo()
    {
        var program = Heard(Beats);
        var clock = Primed();

        var heard = Run(program, Memory(program), clock, 10 * TickSamples, (i, now) =>
        {
            if (i == 0) clock.Start();
            if (i % TickSamples == 0) clock.Tick(now);
        });

        for (var i = 5 * TickSamples; i < 10 * TickSamples; i++)
            (heard[i] - heard[i - 1]).ShouldBe(BeatsPerSecond / Rate, 1e-5, $"sample {i}");

        // The ninth tick has just landed, and the first was the beat itself.
        heard[8 * TickSamples].ShouldBe(8d / MidiSignal.TicksPerBeat, 0.002);
    }

    /// <summary>
    /// A tick lands wherever the cable and the speakers' buffer put it. The beat
    /// is not allowed to jump for that: it slows or hurries a little until it is
    /// back on the line.
    /// </summary>
    [Fact]
    public void A_tick_that_lands_late_is_absorbed_rather_than_jumped()
    {
        var program = Heard(Beats);
        var clock = Primed();

        var heard = Run(program, Memory(program), clock, 20 * TickSamples, (i, now) =>
        {
            if (i == 0) clock.Start();

            // The eleventh tick a quarter of a tick late, the twelfth on time.
            var late = i == 10 * TickSamples + TickSamples / 4;
            var ordinary = i % TickSamples == 0 && i != 10 * TickSamples;

            if (late || ordinary) clock.Tick(now);
        });

        for (var i = 5 * TickSamples; i < 20 * TickSamples; i++)
        {
            var step = heard[i] - heard[i - 1];

            step.ShouldBeGreaterThan(0d, $"sample {i}");
            // Well under the five samples' worth the late tick would have jumped by.
            step.ShouldBeLessThan(2d * BeatsPerSecond / Rate, $"sample {i}");
        }
    }

    [Fact]
    public void A_start_snaps_the_beat_to_nought_and_fires_reset_once()
    {
        var program = Heard(Beats);
        var pulses = Heard(Reset);
        var clock = Primed();
        var restart = 30 * TickSamples;

        void Drive(int i, double now)
        {
            if (i == 0 || i == restart) clock.Start();
            if (i % TickSamples == 0) clock.Tick(now);
        }

        var beats = Run(program, Memory(program), clock, restart + 2, Drive);

        beats[restart - 1].ShouldBeGreaterThan(1d);
        beats[restart].ShouldBe(0d, 1e-6);

        clock = Primed();
        var reset = Run(pulses, Memory(pulses), clock, restart + 2, Drive);

        reset[restart - 1].ShouldBe(0d);
        reset[restart].ShouldBe(1d);
        reset[restart + 1].ShouldBe(0d);
    }

    /// <summary>
    /// A Stop arrives a little after the last tick, so the line has run on by
    /// that much and eases back; a Continue arrives between ticks, and the beats
    /// go on from where they held rather than from where the line would have got
    /// to had it never stopped.
    /// </summary>
    [Fact]
    public void Stopping_holds_the_beats_and_continuing_moves_them_on()
    {
        var program = Heard(Beats);
        var clock = Primed();
        var stop = 24 * TickSamples + 1;
        var go = 40 * TickSamples + TickSamples / 2;

        var heard = Run(program, Memory(program), clock, 50 * TickSamples, (i, now) =>
        {
            if (i == 0) clock.Start();
            if (i == stop) clock.Stop();
            if (i == go) clock.Continue();
            if (i % TickSamples == 0) clock.Tick(now);
        });

        for (var i = stop + 1; i < go; i++) heard[i].ShouldBe(1d, 0.003, $"sample {i}");

        heard[go].ShouldBe(1d, 0.003);
        heard[go + 5 * TickSamples].ShouldBe(1d + 5d * TickSamples / Rate * BeatsPerSecond, 0.01);
    }

    /// <summary>
    /// A box that was silent until its Start has no tempo to run the line at, so
    /// the first tick's worth is caught up over the settling time rather than
    /// jumped.
    /// </summary>
    [Fact]
    public void A_start_before_the_tempo_is_known_catches_up_without_a_jump()
    {
        var program = Heard(Beats);
        var clock = new MidiClock();

        var heard = Run(program, Memory(program), clock, 60 * TickSamples, (i, now) =>
        {
            if (i == 0) clock.Start();
            if (i % TickSamples == 0) clock.Tick(now);
        });

        for (var i = 1; i < 60 * TickSamples; i++)
        {
            var step = heard[i] - heard[i - 1];

            step.ShouldBeGreaterThanOrEqualTo(0d, $"sample {i}");
            step.ShouldBeLessThan(2d * BeatsPerSecond / Rate, $"sample {i}");
        }

        heard[^1].ShouldBe(59d / MidiSignal.TicksPerBeat + BeatsPerSecond * (TickSamples - 1) / Rate, 0.003);
    }

    [Fact]
    public void The_tempo_and_the_transport_are_read_straight_out()
    {
        var bpm = Heard(Bpm);
        var running = Heard(Running);
        var clock = Primed();

        void Drive(int i, double now)
        {
            if (i == TickSamples) clock.Start();
            if (i % TickSamples == 0) clock.Tick(now);
        }

        Run(bpm, Memory(bpm), clock, 3 * TickSamples, Drive)[^1].ShouldBe(125d, 0.01);

        clock = Primed();
        var ran = Run(running, Memory(running), clock, 3 * TickSamples, Drive);

        ran[0].ShouldBe(0d);
        ran[^1].ShouldBe(1d);
    }

    /// <summary>An export has nobody at the machine and no memory to run a line from, and reads the beat as it was published.</summary>
    [Fact]
    public void Without_a_memory_the_beat_is_read_as_published()
    {
        var program = Heard(Beats);
        var clock = new MidiClock();

        var heard = Run(program, null, clock, 3 * TickSamples, (i, now) =>
        {
            if (i == 0) clock.Start();
            if (i % TickSamples == 0) clock.Tick(now);
        });

        heard[2 * TickSamples + 1].ShouldBe(2d / MidiSignal.TicksPerBeat, 1e-6);
        heard[2 * TickSamples + 2].ShouldBe(2d / MidiSignal.TicksPerBeat, 1e-6);
    }

    // ---- helpers --------------------------------------------------------------

    /// <summary>A patch of one Clock In following the box, wired to the ear through the given output.</summary>
    private static CompiledPatch Heard(int port)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = builder.Add(NodeCatalog.ClockTypeId, 0, 0);
        clock.SetState(MidiClockExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiClockExtra.DeviceField] = Box,
        });
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        builder.Wire(clock, port, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
    }

    private static DelayState Memory(CompiledPatch program) =>
        new(program.DelayLengths, 48_000, program.PhaseCount, program.UnitCount, program.TraceCount);

    /// <summary>A clock that has heard the box tick while stopped, as a real one has, so its tempo is known at Start.</summary>
    private static MidiClock Primed()
    {
        var clock = new MidiClock();

        clock.Tick(-2d * Tick);
        clock.Tick(-Tick);

        return clock;
    }

    /// <summary>
    /// Runs the program for <paramref name="samples"/> evaluations at <see cref="Rate"/>,
    /// letting <paramref name="drive"/> work the instrument before each one and
    /// writing the clock into the block after it.
    /// </summary>
    private static double[] Run(CompiledPatch program, DelayState? memory, MidiClock clock, int samples, Action<int, double> drive)
    {
        var live = new LiveValues(program.LiveInputs);
        var registers = program.AllocateRegisters();
        var heard = new double[samples];

        for (var i = 0; i < samples; i++)
        {
            var now = i / Rate;

            drive(i, now);
            clock.WriteTo(live, Box);
            program.Evaluate(0d, 0d, now, registers, default, memory, live: live);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }
}
