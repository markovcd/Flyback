using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The String: plucked through Coordinates' x, excited through y, with real state behind it.
/// </summary>
public class StringTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private const int In = 0;
    private const int Trigger = 1;
    private const int Freq = 2;
    private const int Decay = 3;
    private const int Brightness = 4;

    private static readonly ModuleCatalog Catalog = NodeCatalog.BuiltIn;

    [Fact]
    public void The_engine_offers_it_beside_the_oscillators_for_audio()
    {
        var def = Catalog.Require(NodeCatalog.StringTypeId);

        def.Name.ShouldBe("String");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "string").ShouldBe(1);
    }

    [Fact]
    public void It_keeps_one_line_a_lowest_period_long()
    {
        var program = Wired().CompileForAudio(Catalog).Program;

        program.DelayLengths.ShouldBe([1f / 20f]);
    }

    [Fact]
    public void Left_alone_it_is_silent()
    {
        Play(Rate / 2, pluckAt: -1).ShouldAllBe(s => s == 0f);
    }

    [Theory]
    [InlineData(110f)]
    [InlineData(220f)]
    [InlineData(440f)]
    [InlineData(880f)]
    public void A_pluck_rings_at_the_pitch_asked_for(float hz)
    {
        var output = Play(Rate / 2, knobs: [(Freq, hz), (Decay, 0.3f), (Brightness, 0.5f)]);

        Pitch(output[(Rate / 10)..(Rate / 2)]).ShouldBe(hz, hz * 0.01f);
    }

    [Fact]
    public void Decay_is_how_long_it_takes_to_fall_sixty_decibels()
    {
        // One second: -30 dB half a second on, a little more because reading between two samples dulls each pass.
        var output = Play(Rate, knobs: [(Freq, 220f), (Decay, 0f), (Brightness, 1f)]);

        var early = Rms(output[(Rate / 20)..(Rate / 10)]);
        var later = Rms(output[(Rate * 11 / 20)..(Rate * 6 / 10)]);

        (later / early).ShouldBeInRange(0.01f, 0.04f);
    }

    [Fact]
    public void A_darker_string_loses_its_top_sooner()
    {
        var dark = Play(Rate / 2, knobs: [(Freq, 220f), (Brightness, 0f)])[(Rate / 4)..(Rate / 2)];
        var bright = Play(Rate / 2, knobs: [(Freq, 220f), (Brightness, 1f)])[(Rate / 4)..(Rate / 2)];

        Edge(dark).ShouldBeLessThan(Edge(bright) * 0.75f);
    }

    [Fact]
    public void A_held_trigger_plucks_once()
    {
        var once = Play(Rate / 2, pluckFor: Rate / 2, knobs: [(Decay, -1f)]);

        Rms(once[(Rate * 4 / 10)..]).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void In_bows_it_for_as_long_as_it_is_fed()
    {
        var bowed = Play(Rate / 2, pluckAt: -1, bow: 0.1f, knobs: [(Freq, 220f), (Decay, -1f)]);

        Rms(bowed[(Rate * 4 / 10)..]).ShouldBeGreaterThan(0.01f);
        Pitch(bowed[(Rate / 4)..]).ShouldBe(220f, 220f * 0.02f);
    }

    [Fact]
    public void It_stays_bounded_whatever_it_is_asked()
    {
        foreach (var value in new[] { -1e6f, -1f, 0f, 1f, 1e6f })
        {
            var output = Play(Rate / 5, knobs: [(Freq, value), (Decay, value), (Brightness, value)]);
            output.ShouldAllBe(s => float.IsFinite(s) && MathF.Abs(s) < 8f);
        }
    }

    [Fact]
    public void On_the_picture_in_passes_through()
    {
        var program = Wired(NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        foreach (var y in new[] { -0.5f, 0f, 0.75f })
        {
            program.Evaluate(1f, y, 0d, registers, default);
            ((float)registers[program.OutputBase]).ShouldBe(y, 1e-6f);
        }
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>Plucked at <paramref name="pluckAt"/> (or never, at -1), with <paramref name="bow"/> noise on 'in'.</summary>
    private static float[] Play(
        int length, int pluckAt = 0, int pluckFor = 1, float bow = 0f, (int Port, float Value)[]? knobs = null)
    {
        var program = Wired(NodeCatalog.OutputLeftPort, knobs ?? []).CompileForAudio(Catalog).Program;
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var random = new Random(7);
        var output = new float[length];

        for (var i = 0; i < length; i++)
        {
            var trigger = pluckAt >= 0 && i >= pluckAt && i < pluckAt + pluckFor ? 1f : 0f;
            var excite = bow * (float)(random.NextDouble() * 2 - 1);

            program.Evaluate(trigger, excite, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    private static Patch Wired(int into = NodeCatalog.OutputLeftPort, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = NodeInstance.Create(Catalog.Require(NodeCatalog.CoordTypeId), 0, 0);
        var pluck = NodeInstance.Create(Catalog.Require(NodeCatalog.StringTypeId), 0, 0);
        foreach (var (port, value) in knobs) pluck.InputValues[port] = value;

        var sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputGainPort] = 1f;

        patch.Nodes.Add(coord);
        patch.Nodes.Add(pluck);
        patch.Nodes.Add(sink);
        patch.Connect(coord.Id, NodeCatalog.CoordXPort, pluck.Id, Trigger);
        patch.Connect(coord.Id, NodeCatalog.CoordYPort, pluck.Id, In);
        patch.Connect(pluck.Id, 0, sink.Id, into);

        return patch;
    }

    /// <summary>The frequency whose period best matches the signal to itself, interpolated between lags.</summary>
    private static float Pitch(float[] signal)
    {
        var best = 0;
        var bestScore = double.MinValue;
        var scores = new double[Rate / 40 + 2];

        for (var lag = Rate / 2000; lag < scores.Length - 1; lag++)
        {
            double sum = 0;
            for (var i = 0; i + lag < signal.Length; i++) sum += signal[i] * signal[i + lag];
            scores[lag] = sum / (signal.Length - lag);

            if (scores[lag] > bestScore) (best, bestScore) = (lag, scores[lag]);
        }

        // Correlation also peaks at every multiple of the period; take the shortest lag that nearly matches.
        for (var lag = Rate / 2000; lag < best; lag++)
            if (scores[lag] > 0.9 * bestScore && scores[lag] >= scores[lag - 1] && scores[lag] >= scores[lag + 1])
            {
                best = lag;
                break;
            }

        var (a, b, c) = (scores[best - 1], scores[best], scores[best + 1]);
        var offset = 0.5 * (a - c) / (a - 2 * b + c);

        return (float)(Rate / (best + offset));
    }

    private static float Rms(float[] signal) => MathF.Sqrt(signal.Average(s => s * s));

    /// <summary>How much of the signal is sample-to-sample change, relative to its size.</summary>
    private static float Edge(float[] signal) =>
        MathF.Sqrt(signal.Skip(1).Zip(signal, (b, a) => (b - a) * (b - a)).Average()) / Rms(signal);
}
