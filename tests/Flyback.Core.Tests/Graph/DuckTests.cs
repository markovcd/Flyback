using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

public class DuckTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private const int Key = 2;
    private const int Depth = 3;
    private const int Full = 4;
    private const int Release = 6;

    private const int Gain = 2;

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_shaping_for_audio()
    {
        var def = Modules.Get(NodeCatalog.DuckTypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Duck");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    [Fact]
    public void Unkeyed_it_is_a_wire()
    {
        var signal = Sine(440d, 0.9f, 0.2);
        var (left, right) = Play(signal, knobs: [(Depth, 1f)]);

        left.ShouldBe(signal);
        right.ShouldBe(signal, "an unpatched right carries the left");
    }

    [Fact]
    public void A_key_at_full_holds_it_down_by_the_depth()
    {
        var (left, gain) = Play(Hold(0.5f, 0.3), Hold(1f, 0.3), into: (0, Key), heard: (0, Gain), knobs: [(Depth, 0.6f)]);

        gain[^1].ShouldBe(0.4f, 1e-4f);
        left[^1].ShouldBe(0.2f, 1e-4f);
    }

    [Fact]
    public void A_quieter_key_ducks_less_and_full_says_what_counts_as_loud()
    {
        var half = Play(Hold(0.5f, 0.3), Hold(0.5f, 0.3), into: (0, Key), heard: (0, Gain), knobs: [(Depth, 0.6f)]);
        var loud = Play(Hold(0.5f, 0.3), Hold(0.5f, 0.3), into: (0, Key), heard: (0, Gain), knobs: [(Depth, 0.6f), (Full, 0.5f)]);

        half.Right[^1].ShouldBe(0.7f, 1e-4f);
        loud.Right[^1].ShouldBe(0.4f, 1e-4f);
    }

    /// <summary>A kick's own sound: a burst of loud sine, then nothing.</summary>
    [Fact]
    public void It_comes_back_up_over_the_release()
    {
        var burst = Sine(60d, 1f, 0.1).Concat(Hold(0f, 0.5)).ToArray();
        var (_, gain) = Play(Hold(0.5f, 0.6), burst, into: (0, Key), heard: (0, Gain), knobs: [(Depth, 1f), (Release, -1f)]);

        gain[Rate / 10].ShouldBeLessThan(0.5f);
        gain[Rate / 10 + Rate / 10].ShouldBeGreaterThan(gain[Rate / 10]);
        gain[^1].ShouldBe(1f, 1e-2f, "five time constants of release");
    }

    [Fact]
    public void It_starts_at_full_level_rather_than_fading_in()
    {
        var (left, _) = Play(Hold(0.5f, 0.01), Hold(0f, 0.01), into: (0, Key));

        left[0].ShouldBe(0.5f);
    }

    [Fact]
    public void On_the_picture_it_is_a_wire()
    {
        float[] values = [-0.75f, 0f, 0.5f, 1f];

        var b = new PatchBuilder(Modules);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var duck = b.Add(NodeCatalog.DuckTypeId, (Depth, 1f));
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(coord, 0, duck, 0);
        b.Wire(coord, 0, duck, Key);
        b.Wire(duck, 0, sink, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();

        values.Select(value =>
        {
            program.Evaluate(value, 0f, 0f, registers, default);
            return (float)registers[program.OutputBase];
        }).ShouldBe(values);
    }

    /// <summary>
    /// Plays x and y through a Duck sample by sample, with memory behind it: x is
    /// Coordinates' x and y its y, and two of its outputs are what the speakers hear.
    /// </summary>
    private static (float[] Left, float[] Right) Play(
        float[] x,
        float[]? y = null,
        (int X, int Y)? into = null,
        (int Left, int Right)? heard = null,
        params (int Port, float Value)[] knobs)
    {
        var (toX, toY) = into ?? (0, -1);
        var (left, right) = heard ?? (0, 1);

        var b = new PatchBuilder(Modules);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var duck = b.Add(NodeCatalog.DuckTypeId, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, 0, duck, toX);
        if (toY >= 0) b.Wire(coord, 1, duck, toY);
        b.Wire(duck, left, sink, NodeCatalog.OutputLeftPort);
        b.Wire(duck, right, sink, NodeCatalog.OutputRightPort);

        var result = b.Patch.CompileForAudio(Modules);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var heardLeft = new float[x.Length];
        var heardRight = new float[x.Length];

        for (var i = 0; i < x.Length; i++)
        {
            program.Evaluate(x[i], y?[i] ?? 0f, i / (double)Rate, registers, default, state);
            heardLeft[i] = (float)registers[program.OutputBase];
            heardRight[i] = (float)registers[program.OutputBase + 1];
        }

        return (heardLeft, heardRight);
    }

    private static float[] Sine(double hertz, float amplitude, double seconds) =>
        [.. Enumerable.Range(0, (int)(seconds * Rate)).Select(i => amplitude * (float)Math.Sin(2d * Math.PI * hertz * i / Rate))];

    private static float[] Hold(float value, double seconds) => [.. Enumerable.Repeat(value, (int)(seconds * Rate))];
}
