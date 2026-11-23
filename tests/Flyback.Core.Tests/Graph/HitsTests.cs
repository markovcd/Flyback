using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Tests;
using Shouldly;
using static Flyback.Tests.DrumLoop;

namespace Flyback.Core.Tests.Graph;

public class HitsTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_measurement_for_audio()
    {
        var def = Modules.Get(HitsModule.TypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Hits");
        def.Category.ShouldBe(ModuleCategories.Measurement);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    [Fact]
    public void A_kick_every_half_second_is_heard_as_one_kick_each_and_nothing_else()
    {
        var loop = DrumLoop.Mix(4, (Kick, Every(0.5, 4)));

        Hits(loop, HitsModule.KickOut).ShouldBe(8);
        Hits(loop, HitsModule.SnareOut).ShouldBe(0);
        Hits(loop, HitsModule.HatsOut).ShouldBe(0);
    }

    [Fact]
    public void Each_drum_of_a_loop_opens_its_own_gate()
    {
        var loop = Drums(4, 1f);

        Hits(loop, HitsModule.KickOut).ShouldBe(8, "kicks on every beat");
        Hits(loop, HitsModule.SnareOut).ShouldBe(4, "snares on two and four");
        Hits(loop, HitsModule.HatsOut).ShouldBe(16, "hats on the eighths");
    }

    [Fact]
    public void A_quiet_loop_is_heard_the_same_as_a_loud_one()
    {
        var quiet = Drums(4, 0.03f);

        Hits(quiet, HitsModule.KickOut).ShouldBe(8);
        Hits(quiet, HitsModule.SnareOut).ShouldBe(4);
        Hits(quiet, HitsModule.HatsOut).ShouldBe(16);
    }

    [Fact]
    public void A_held_bass_note_is_one_hit_where_it_starts_and_no_more()
    {
        var bass = Tone(60d, 0.5f, 3);

        Hits(bass, HitsModule.KickOut).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Kicks_over_a_held_bass_note_are_still_heard()
    {
        var bass = Tone(55d, 0.25f, 4);
        var loop = DrumLoop.Mix(4, (Kick, Every(0.5, 4)));
        var both = bass.Zip(loop, (a, b) => a + b).ToArray();

        // The first kick lands with the bass's own start.
        Hits(both, HitsModule.KickOut).ShouldBe(8);
    }

    [Fact]
    public void Two_hits_closer_than_the_rest_are_heard_once()
    {
        var flam = DrumLoop.Mix(1, (Snare, [0.2, 0.23]));

        Hits(flam, HitsModule.SnareOut).ShouldBe(1);
    }

    [Fact]
    public void Silence_opens_nothing()
    {
        var silence = new float[Rate];

        Hits(silence, HitsModule.KickOut).ShouldBe(0);
        Hits(silence, HitsModule.SnareOut).ShouldBe(0);
        Hits(silence, HitsModule.HatsOut).ShouldBe(0);
    }

    [Fact]
    public void A_gate_stays_open_for_the_length()
    {
        var gate = Play(DrumLoop.Mix(1, (Kick, [0.2])), HitsModule.KickOut, (HitsModule.LengthPort, -1f));

        ((double)gate.Count(open => open > 0.5f)).ShouldBe(Rate / 10d, Rate / 100d);
    }

    [Fact]
    public void A_level_reaches_the_top_on_a_hit_and_falls_after()
    {
        var level = Play(DrumLoop.Mix(2, (Kick, Every(0.5, 2))), HitsModule.KickLevelOut);

        level.Max().ShouldBe(1f, 0.01f);
        level[(int)(0.45 * Rate)].ShouldBeLessThan(0.2f);
    }

    [Fact]
    public void On_the_picture_it_is_silent()
    {
        var b = new PatchBuilder(Modules);
        var coord = b.Add(SourceModules.CoordTypeId);
        var hits = b.Add(HitsModule.TypeId);
        var sink = b.Add(OutputModule.TypeId);
        b.Wire(coord, 0, hits, HitsModule.InPort);
        b.Wire(hits, HitsModule.KickLevelOut, sink, OutputModule.ColorPort);

        var program = b.Patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0.9f, 0f, 0f, registers, default);
        ((float)registers[program.OutputBase]).ShouldBe(0f);
    }

    /// <summary>How many times <paramref name="output"/> rises past a half.</summary>
    private static int Hits(float[] sound, int output, params (int Port, float Value)[] knobs)
    {
        var played = Play(sound, output, knobs);
        var rises = 0;

        for (var i = 1; i < played.Length; i++)
            if (played[i - 1] <= 0.5f && played[i] > 0.5f)
                rises++;

        return rises;
    }

    /// <summary>Plays a sound through a Hits sample by sample, as Coordinates' x, and reads one output.</summary>
    private static float[] Play(float[] sound, int output, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Modules);
        var coord = b.Add(SourceModules.CoordTypeId);
        var hits = b.Add(HitsModule.TypeId, knobs);
        var sink = b.Add(OutputModule.TypeId, (OutputModule.VolumePort, 1f));

        b.Wire(coord, 0, hits, HitsModule.InPort);
        b.Wire(hits, output, sink, OutputModule.LeftPort);

        var result = b.Patch.CompileForAudio(Modules);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var heard = new float[sound.Length];

        for (var i = 0; i < sound.Length; i++)
        {
            program.Evaluate(sound[i], 0f, i / (double)Rate, registers, default, state);
            heard[i] = (float)registers[program.OutputBase];
        }

        return heard;
    }
}
