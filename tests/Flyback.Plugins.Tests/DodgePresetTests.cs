using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Dodge, played by a script: a strike starts a run, a wall in your lane ends it,
/// and a player who always finds the gap keeps going.
/// </summary>
public class DodgePresetTests
{
    private const int Rate = GlobalConstants.SampleRate;

    /// <summary>When the scripted player strikes its first key, which starts the run.</summary>
    private const double Starts = 1.0;

    private static readonly PluginCatalog Loaded = PluginHost.Load();

    private static readonly int[] White = [0, 2, 4, 5, 7, 9, 11];

    private static Patch Patch() => Loaded.Presets.Single(p => p.Name == "Dodge").Build(Loaded.Modules);

    [Fact]
    public void It_is_a_showcase_from_the_picture_plugin()
    {
        Loaded.Presets.Single(p => p.Name == "Dodge").Kind.ShouldBe(PresetKind.Showcase);
        Presets.All.ShouldNotContain(p => p.Name == "Dodge");
    }

    [Fact]
    public void Both_sinks_compile_clean_and_read_the_keys()
    {
        var patch = Patch();

        foreach (var compiled in new[]
                 {
                     patch.CompileForAudio(Loaded.Modules, played: true),
                     patch.CompileForVideo(Loaded.Modules, played: true),
                 })
        {
            compiled.Issues.ShouldBeEmpty(string.Join("; ", compiled.Issues.Select(i => i.Message)));
            compiled.Program.LiveInputs.ShouldContain(k => k.EndsWith("/" + MidiSignal.Pitch));
            compiled.Program.LiveInputs.ShouldContain(k => k.EndsWith("/" + MidiSignal.Gate));
        }
    }

    /// <summary>
    /// A cut wire is a cell, which holds ±16, and how far the walls have come passes
    /// sixteen in the first quarter-minute. Only the game's own state may be cut.
    /// </summary>
    [Fact]
    public void Every_loop_is_cut_on_a_wire_out_of_a_cell_that_feeds_itself()
    {
        var patch = Patch();
        var backwards = Cycles.Backwards(patch);
        var cells = backwards.Where(w => w.SourceNode == w.TargetNode).Select(w => w.SourceNode).ToHashSet();

        cells.Count.ShouldBe(3);
        backwards.ShouldAllBe(w => cells.Contains(w.SourceNode));
    }

    [Fact]
    public void Standing_still_loses_to_the_second_wall()
    {
        var ends = Play(bot: false, seconds: 6);

        // The first gap is in the lane the strike put the player in.
        Gap(0).ShouldBe(0);
        ends.Count.ShouldBe(1);
        ends[0].ShouldBe(Starts + Arrives(1), 0.05);
    }

    [Fact]
    public void Finding_every_gap_survives()
    {
        Play(bot: true, seconds: 30).ShouldBeEmpty();
    }

    [Fact]
    public void The_sound_thuds_when_the_picture_flashes()
    {
        var picture = Play(bot: false, seconds: 6).Single();

        var heard = Heard(bot: false, seconds: 6);
        heard.ShouldBe(picture, 0.05);
    }

    [Fact]
    public void A_player_who_finds_every_gap_hears_no_thud()
    {
        Heard(bot: true, seconds: 20).ShouldBe(-1);
    }

    // --- harness -----------------------------------------------------------------

    private static int Gap(int row)
    {
        var m = row % 997;
        var h = ((m * m % 997) * 73 + m * 151 + 17) % 997;
        return Math.Min(h * 7 / 997, 6);
    }

    /// <summary>How far the walls have come, in rows short of the player, <paramref name="g"/> seconds into a run.</summary>
    private static double Rows(double g)
    {
        var h = Math.Min(g, 70);
        return 1.2 * h + 0.02 * h * h + 4 * (g - h) - 3;
    }

    /// <summary>How long into a run <paramref name="row"/> reaches the player.</summary>
    private static double Arrives(int row) => (-1.2 + Math.Sqrt(1.44 + 0.08 * (row + 3))) / 0.04;

    /// <summary>A strike on Z, then either nothing or, if <paramref name="bot"/>, the key of every gap once the wall before it has passed.</summary>
    private sealed class Keys(bool bot)
    {
        private float pitch = 60, gate, velocity, strikes;
        private int lane = -1;
        private double releaseAt = -1;

        public void At(double t, LiveValues live, IEnumerable<string> keys)
        {
            if (t >= Starts && strikes == 0) Strike(0, t);
            if (releaseAt >= 0 && t >= releaseAt) (gate, releaseAt) = (0, -1);

            if (bot && t >= Starts)
            {
                var rows = Rows(t - Starts);
                var next = (int)Math.Floor(rows);
                if (rows - next >= 0.3) next++;
                if (next >= 0 && Gap(next) != lane) Strike(Gap(next), t);
            }

            foreach (var key in keys)
            {
                var signal = key[(key.LastIndexOf('/') + 1)..];
                live.Set(key, signal switch
                {
                    MidiSignal.Pitch => pitch,
                    MidiSignal.Gate => gate,
                    MidiSignal.Velocity => velocity,
                    _ => strikes,
                });
            }
        }

        private void Strike(int to, double t)
        {
            (lane, pitch, gate, velocity) = (to, 60 + White[to], 1, 0.8f);
            strikes++;
            releaseAt = t + 0.08;
        }
    }

    /// <summary>When each run ended, as the picture drew it: the frame the red flash first shows in.</summary>
    private static List<double> Play(bool bot, double seconds)
    {
        var patch = Patch();
        var program = patch.CompileForVideo(Loaded.Modules, played: true).Program;
        var live = new LiveValues(program.LiveInputs);
        patch.Seed(live);

        var keys = new Keys(bot);
        var renderer = new SynthRenderer();
        const int width = 32, height = 18;
        var frame = new byte[width * height * 4];
        var ends = new List<double>();
        var flashing = false;

        for (var i = 0; i < seconds * 60; i++)
        {
            var t = i / 60.0;
            keys.At(t, live, program.LiveInputs);
            renderer.Render(program, t, width, height, frame, width * 4, live);

            // Red at the left edge, outside the lanes, is only ever the flash.
            var at = (height / 2 * width) * 4;
            var red = frame[at + 2] > 40 && frame[at + 1] < 20;
            if (red && !flashing) ends.Add(t);
            flashing = red;
        }

        return ends;
    }

    /// <summary>When the first thud is heard, or -1: the output's low end, under the plucks and the chimes.</summary>
    private static double Heard(bool bot, double seconds)
    {
        var patch = Patch();
        var program = patch.CompileForAudio(Loaded.Modules, played: true).Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState(program, Rate);
        var live = new LiveValues(program.LiveInputs);
        patch.Seed(live);

        var keys = new Keys(bot);
        double low = 0, lower = 0;

        for (var i = 0; i < seconds * Rate; i++)
        {
            var t = i / (double)Rate;
            if (i % 64 == 0) keys.At(t, live, program.LiveInputs);

            program.Evaluate(0, 0, t, registers, default, state, live: live);

            // Two poles at 90 Hz: the thud starts at 160 and falls, the lowest pluck is 262.
            low += (registers[program.OutputBase] - low) * (2 * Math.PI * 90 / Rate);
            lower += (low - lower) * (2 * Math.PI * 90 / Rate);
            if (Math.Abs(lower) > 0.1) return t;
        }

        return -1;
    }
}
