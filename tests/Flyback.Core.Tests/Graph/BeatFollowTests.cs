using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;
using static Flyback.Core.Graph.BeatFollowModule;

namespace Flyback.Core.Tests.Graph;

public class BeatFollowTests
{
    /// <summary>Evaluations a second: coarse beside the speakers', to keep twenty seconds of music quick.</summary>
    private const int Rate = 4_000;

    /// <summary>How long each hit holds its gate open, as a Hits' does.</summary>
    private const double HitLength = 0.05;

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_timing_for_the_sound()
    {
        var def = Modules.Get(TypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Beat Follow");
        def.Category.ShouldBe(ModuleCategories.Timing);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    [Fact]
    public void A_steady_kick_gives_its_tempo_and_a_beat_on_every_kick()
    {
        var kicks = Every(0.5, from: 0.2, until: 12);
        var run = Follow(Gate(kicks, 12));

        run.Bpm[^1].ShouldBe(120f, 0.5f);
        run.Locked[^1].ShouldBe(1f);
        BeatsNear(run.Beat, kicks.Where(k => k > 4)).ShouldAllBe(error => error < 0.01);
    }

    [Theory]
    [InlineData(1.0, 120f)]
    [InlineData(0.25, 120f)]
    [InlineData(0.4, 150f)]
    public void A_tempo_outside_the_range_is_folded_into_it(double gap, float bpm)
    {
        var run = Follow(Gate(Every(gap, from: 0.2, until: 10), 10));

        run.Bpm[^1].ShouldBe(bpm, 0.5f);
    }

    [Fact]
    public void A_hit_off_the_beat_leaves_the_tempo_alone()
    {
        // Four on the floor at 120 with an extra kick on the and of three in every bar.
        var kicks = Every(0.5, from: 0.2, until: 12).Concat(Every(2, from: 1.45, until: 12)).Order().ToArray();
        var run = Follow(Gate(kicks, 12));

        run.Bpm[^1].ShouldBe(120f, 0.5f);
        BeatsNear(run.Beat, kicks.Where(k => k > 4 && Math.Abs((k - 0.2) / 0.5 - Math.Round((k - 0.2) / 0.5)) < 0.01)).ShouldAllBe(error => error < 0.01);
    }

    [Fact]
    public void Stray_hits_between_the_kicks_leave_the_beat_alone()
    {
        // What a kick detector hears of a busy mix: the kicks, and a hit somewhere off the beat in most of them.
        var random = new Random(7);
        var kicks = Every(0.5, from: 0.2, until: 16);
        var strays = kicks.Where(_ => random.NextDouble() < 0.7).Select(k => k + 0.5 * (0.25 + 0.5 * random.NextDouble()));
        var run = Follow(Gate(kicks.Concat(strays).Order(), 16));

        run.Bpm[^1].ShouldBe(120f, 0.5f);
        BeatsNear(run.Beat, kicks.Where(k => k > 6)).ShouldAllBe(error => error < 0.01);
    }

    [Fact]
    public void In_a_break_it_carries_on_at_the_last_tempo_and_says_it_has_lost_the_beat()
    {
        var kicks = Every(0.5, from: 0.2, until: 8);
        var run = Follow(Gate(kicks, 14));

        run.Bpm[^1].ShouldBe(120f, 0.5f);
        run.Locked[^1].ShouldBe(0f);

        var after = Every(0.5, from: kicks[^1] + 0.5, until: 14).Take(10).ToArray();
        BeatsNear(run.Beat, after).ShouldAllBe(error => error < 0.01);
    }

    [Fact]
    public void A_tempo_that_drifts_is_followed()
    {
        var kicks = Drifting(120, 128, seconds: 20);
        var run = Follow(Gate(kicks, 20));

        run.Bpm[^1].ShouldBe(128f, 1f);
        run.Locked[^1].ShouldBe(1f);
        BeatsNear(run.Beat, kicks.Where(k => k > 15)).ShouldAllBe(error => error < 0.02);
    }

    [Fact]
    public void A_new_tempo_makes_it_start_over()
    {
        var kicks = Every(0.5, from: 0.2, until: 8).Concat(Every(60.0 / 96, from: 8.2, until: 18)).ToArray();
        var run = Follow(Gate(kicks, 18));

        run.Bpm[^1].ShouldBe(96f, 0.5f);
        run.Reset.Count(r => r > 0.5f).ShouldBe(2, "once on first locking and once at the new tempo");
        BeatsNear(run.Beat, kicks.Where(k => k > 14)).ShouldAllBe(error => error < 0.01);
    }

    [Fact]
    public void Its_beats_only_ever_count_forward_once_locked()
    {
        var jittered = Every(0.5, from: 0.2, until: 12).Select((k, i) => k + (i % 3 - 1) * 0.012).ToArray();
        var run = Follow(Gate(jittered, 12));
        var first = Array.FindIndex(run.Reset, r => r > 0.5f);

        for (var i = first + 1; i < run.Beats.Length; i++)
            run.Beats[i].ShouldBeGreaterThanOrEqualTo(run.Beats[i - 1]);

        // Read as a float, a phase just short of 1 rounds up to it.
        run.Phase.ShouldAllBe(p => p >= 0f && p <= 1f);
    }

    [Fact]
    public void Nudge_runs_the_beat_ahead_of_the_kicks()
    {
        var kicks = Every(0.5, from: 0.2, until: 10);
        var run = Follow(Gate(kicks, 10), (NudgePort, 50f));

        var heard = Times(run.Beat).Where(t => t > 5).ToArray();
        heard.ShouldAllBe(t => kicks.Any(k => Math.Abs(k - 0.05 - t) < 0.01));
    }

    [Fact]
    public void Before_two_hits_it_stands_still()
    {
        var run = Follow(Gate([0.5], 3));

        run.Bpm.ShouldAllBe(b => b == 0f);
        run.Beat.ShouldAllBe(b => b == 0f);
        run.Locked.ShouldAllBe(l => l == 0f);
    }

    [Fact]
    public void On_the_picture_it_gives_nothing()
    {
        var b = new PatchBuilder(Modules);
        var coord = b.Add(SourceModules.CoordTypeId);
        var follow = b.Add(TypeId);
        var sink = b.Add(OutputModule.TypeId);
        b.Wire(coord, 0, follow, InPort);
        b.Wire(follow, BpmPort, sink, OutputModule.ColorPort);

        var program = b.Patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(1f, 0f, 0f, registers, default);
        ((float)registers[program.OutputBase]).ShouldBe(0f);
    }

    /// <summary>Every output of a Beat Follow played the gate, an evaluation at a time.</summary>
    private sealed record Run(float[] Beats, float[] Phase, float[] Bpm, float[] Locked, float[] Beat, float[] Reset);

    private static Run Follow(float[] gate, params (int Port, float Value)[] knobs) =>
        new(Play(gate, BeatsPort, knobs), Play(gate, PhasePort, knobs), Play(gate, BpmPort, knobs),
            Play(gate, LockedPort, knobs), Play(gate, BeatPort, knobs), Play(gate, ResetPort, knobs));

    private static float[] Play(float[] gate, int output, (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Modules);
        var coord = b.Add(SourceModules.CoordTypeId);
        var follow = b.Add(TypeId, knobs);
        var sink = b.Add(OutputModule.TypeId, (OutputModule.VolumePort, 1f));
        b.Wire(coord, 0, follow, InPort);
        b.Wire(follow, output, sink, OutputModule.LeftPort);

        var result = b.Patch.CompileForAudio(Modules);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var heard = new float[gate.Length];

        for (var i = 0; i < gate.Length; i++)
        {
            program.Evaluate(gate[i], 0f, i / (double)Rate, registers, default, state);
            heard[i] = (float)registers[program.OutputBase];
        }

        return heard;
    }

    /// <summary>A gate open for <see cref="HitLength"/> at each hit.</summary>
    private static float[] Gate(IEnumerable<double> hits, double seconds)
    {
        var gate = new float[(int)(seconds * Rate)];

        foreach (var hit in hits)
            for (var i = (int)Math.Round(hit * Rate); i < Math.Min(gate.Length, (int)Math.Round((hit + HitLength) * Rate)); i++)
                gate[i] = 1f;

        return gate;
    }

    private static double[] Every(double gap, double from, double until) =>
        [.. Enumerable.Range(0, (int)Math.Ceiling((until - from) / gap)).Select(i => from + i * gap).Where(t => t < until)];

    /// <summary>Kicks whose tempo slides evenly from one BPM to another.</summary>
    private static double[] Drifting(double fromBpm, double toBpm, double seconds)
    {
        var kicks = new List<double>();

        for (var t = 0.2; t < seconds; t += 60 / (fromBpm + (toBpm - fromBpm) * Math.Min(1, t / (seconds * 0.6))))
            kicks.Add(t);

        return [.. kicks];
    }

    private static IEnumerable<double> Times(float[] trigger) =>
        trigger.Select((v, i) => (v, i)).Where(p => p.v > 0.5f).Select(p => p.i / (double)Rate);

    /// <summary>For each hit, how far the nearest beat the follower fired is from it, in seconds.</summary>
    private static IEnumerable<double> BeatsNear(float[] trigger, IEnumerable<double> hits)
    {
        var beats = Times(trigger).ToArray();

        return hits.Select(h => beats.Length == 0 ? double.MaxValue : beats.Min(b => Math.Abs(b - h)));
    }
}
