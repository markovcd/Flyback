using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Noise module: white and pink noise, and the stepped and drifting values.
/// </summary>
public class NoiseTests
{
    private const int White = 0;
    private const int Pink = 1;
    private const int Stepped = 2;
    private const int Drift = 3;

    private const int RatePort = 1;
    private const int SeedPort = 2;
    private const int AmpPort = 3;
    private const int BiasPort = 4;

    /// <summary>The oversampled rate the audio path evaluates at.</summary>
    private const double Inner = GlobalConstants.SampleRate * 4;

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_oscillators()
    {
        var def = Modules.Get(NodeCatalog.NoiseTypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Noise");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Outputs.Select(p => p.Name).ShouldBe(["white", "pink", "random", "drift"]);
    }

    /// <summary>The language shortens a type id to its last segment, so this must not collide.</summary>
    [Fact]
    public void Its_short_name_is_its_own()
    {
        Modules.All.Count(d => d.TypeId.Split('.')[^1] == "noise").ShouldBe(1);
    }

    [Fact]
    public void It_needs_no_state()
    {
        var program = Program(White).Compiled;

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
    }

    [Fact]
    public void White_is_bounded_centered_and_uncorrelated()
    {
        var white = Samples(White, 40_000);

        white.ShouldAllBe(s => s >= -1f && s <= 1f);
        white.Average().ShouldBe(0f, 0.03f);
        Correlation(white, 1).ShouldBe(0f, 0.03f);

        for (var i = 1; i < white.Length; i++) white[i].ShouldNotBe(white[i - 1]);
    }

    [Fact]
    public void Pink_is_bounded_and_darker_than_white()
    {
        var pink = Samples(Pink, 40_000);

        pink.ShouldAllBe(s => s >= -1f && s <= 1f);
        pink.Average().ShouldBe(0f, 0.1f);
        Correlation(pink, 1).ShouldBeGreaterThan(0.5f);
    }

    [Fact]
    public void Pink_is_not_far_quieter_than_white()
    {
        var ratio = Rms(Samples(Pink, 40_000)) / Rms(Samples(White, 40_000));

        ratio.ShouldBeInRange(0.4f, 1f);
    }

    [Fact]
    public void Random_holds_for_a_step_and_moves_between_steps()
    {
        var read = Program(Stepped, (RatePort, 4f)).Read;

        read(0.01).ShouldBe(read(0.24));
        read(0.26).ShouldNotBe(read(0.24));

        var values = Enumerable.Range(0, 64).Select(step => read((step + 0.5) / 4)).ToList();
        values.Min().ShouldBeLessThan(-0.5f);
        values.Max().ShouldBeGreaterThan(0.5f);
    }

    [Fact]
    public void Drift_is_smooth_and_passes_through_the_stepped_values()
    {
        var drift = Program(Drift, (RatePort, 4f)).Read;
        var stepped = Program(Stepped, (RatePort, 4f)).Read;

        for (var step = 0; step < 16; step++)
            drift(step / 4.0).ShouldBe(stepped(step / 4.0), 1e-6f);

        for (var ms = 1; ms < 4_000; ms++)
            MathF.Abs(drift(ms / 1000.0) - drift((ms - 1) / 1000.0)).ShouldBeLessThan(0.05f);
    }

    [Fact]
    public void A_different_seed_is_different_noise()
    {
        var first = Samples(White, 20_000);
        var second = Samples(White, 20_000, (SeedPort, 1f));

        Correlation(first, second).ShouldBe(0f, 0.05f);
    }

    [Fact]
    public void Amp_and_bias_apply_to_every_output()
    {
        foreach (var port in new[] { White, Pink, Stepped, Drift })
            Program(port, (AmpPort, 0f), (BiasPort, 0.3f)).Read(1.234).ShouldBe(0.3f, 1e-6f);
    }

    [Fact]
    public void The_picture_hears_the_same_values_as_the_speakers()
    {
        foreach (var port in new[] { White, Pink, Stepped, Drift })
        {
            var audio = Program(port).Read;
            var video = Picture(port);

            foreach (var t in new[] { 0.0, 0.37, 1.5, 12.25, 600.125 })
                video(t).ShouldBe(audio(t), 1e-6f);
        }
    }

    /// <summary>At this time the white lanes and the 64-a-second steps both count past an int.</summary>
    [Fact]
    public void Every_output_is_still_noise_once_its_lattice_counts_past_an_int()
    {
        const double Late = 2e8;

        foreach (var port in new[] { White, Pink, Stepped, Drift })
        {
            var read = Program(port, (RatePort, 64f)).Read;
            var values = Enumerable.Range(0, 256).Select(i => read(Late + (i + 0.3) / 64)).ToList();

            values.ShouldAllBe(v => v >= -1f && v <= 1f, $"output {port}");
            values.Distinct().Count().ShouldBeGreaterThan(200, $"output {port}");
            values.Average().ShouldBe(0f, 0.2f, $"output {port}");
        }
    }

    /// <summary>
    /// Thirteen of the sixteen lookups are pink's rows, and a patch that wants
    /// hiss for a hat runs none of them.
    /// </summary>
    [Fact]
    public void An_output_costs_its_own_lookups_and_no_other_output_s()
    {
        int Lookups(int port) => Program(port).Compiled.Ops.Count(op => op.Code == OpCode.Noise3);

        Lookups(White).ShouldBe(1);
        Lookups(Pink).ShouldBe(13);
        Lookups(Stepped).ShouldBe(1);
        Lookups(Drift).ShouldBe(1);
    }

    // --- harness -----------------------------------------------------------------

    private static float[] Samples(int port, int count, params (int Port, float Value)[] knobs)
    {
        var read = Program(port, knobs).Read;
        return [.. Enumerable.Range(0, count).Select(i => read(1 + i / Inner))];
    }

    /// <summary>One Noise, its <paramref name="port"/> into the left speaker, at full gain.</summary>
    private static (CompiledPatch Compiled, Func<double, float> Read) Program(
        int port, params (int Port, float Value)[] knobs)
    {
        var patch = Wired(port, NodeCatalog.OutputLeftPort, knobs);
        var program = patch.CompileForAudio(Modules).Program;
        var registers = program.AllocateRegisters();

        return (program, t =>
        {
            program.Evaluate(0f, 0f, t, registers, default);
            return (float)registers[program.OutputBase];
        });
    }

    private static Func<double, float> Picture(int port)
    {
        var patch = Wired(port, NodeCatalog.OutputColorPort);
        var program = patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();

        return t =>
        {
            program.Evaluate(0f, 0f, t, registers, default);
            return (float)registers[program.OutputBase];
        };
    }

    private static Patch Wired(int port, int into, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var random = NodeInstance.Create(Modules.Require(NodeCatalog.NoiseTypeId), 0, 0);
        foreach (var (at, value) in knobs) random.InputValues[at] = value;

        var sink = NodeInstance.Create(Modules.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputVolumePort] = 1f;

        patch.Nodes.Add(random);
        patch.Nodes.Add(sink);
        patch.Connect(random.Id, port, sink.Id, into);

        return patch;
    }

    private static float Rms(float[] signal) => MathF.Sqrt(signal.Average(s => s * s));

    private static float Correlation(float[] signal, int lag) =>
        Correlation(signal[..^lag], signal[lag..]);

    private static float Correlation(float[] a, float[] b)
    {
        var meanA = a.Average();
        var meanB = b.Average();

        double cross = 0, varA = 0, varB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            cross += (a[i] - meanA) * (b[i] - meanB);
            varA += (a[i] - meanA) * (a[i] - meanA);
            varB += (b[i] - meanB) * (b[i] - meanB);
        }

        return (float)(cross / Math.Sqrt(varA * varB));
    }
}
