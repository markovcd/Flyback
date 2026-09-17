using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Hiss, read a sample at a time. It is stateless, so no renderer is involved.
/// </summary>
public class HissTests
{
    private const string HissType = "flyback.voice.hiss";

    private const int SeedPort = 1;
    private const int AmpPort = 2;

    private const int Rate = 48_000;
    private const int Length = 48_000;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    [Fact]
    public void The_voice_plugin_offers_it_beside_the_oscillators()
    {
        var def = Catalog.Get(HissType).ShouldNotBeNull();

        def.Name.ShouldBe("Hiss");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Inputs[0].Domain.ShouldBeTrue();
        Catalog.ProviderOf(HissType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "hiss").ShouldBe(1);
    }

    [Fact]
    public void It_fills_the_range_and_sits_on_nought()
    {
        var hiss = Samples();

        hiss.Min().ShouldBeInRange(-1d, -0.99);
        hiss.Max().ShouldBeInRange(0.99, 1d);
        hiss.Average().ShouldBe(0d, 0.02);
    }

    /// <summary>White rather than merely random: one sample says nothing about the next.</summary>
    [Fact]
    public void Neighboring_samples_share_nothing()
    {
        Correlation(Samples(), 1).ShouldBe(0d, 0.03);
        Correlation(Samples(), 7).ShouldBe(0d, 0.03);
    }

    [Fact]
    public void Two_seeds_are_two_noises_and_one_seed_is_one()
    {
        var first = Samples();
        var again = Samples();
        var other = Samples((SeedPort, 1f));

        again.ShouldBe(first);

        var both = first.Zip(other, (a, b) => a * b).Average();
        var power = first.Select(a => a * a).Average();
        (both / power).ShouldBe(0d, 0.03);
    }

    [Fact]
    public void Amp_scales_it()
    {
        var quiet = Samples((AmpPort, 0.25f));

        quiet.Max().ShouldBeInRange(0.24, 0.25);
        quiet.Min().ShouldBeInRange(-0.25, -0.24);
    }

    /// <summary>
    /// The five modules a preset built this from — a Multiply, a Sin, a Multiply, a
    /// Fraction and a Remap — against this, equal rather than close.
    /// </summary>
    [Fact]
    public void It_is_the_five_modules_it_stands_for_to_the_last_bit()
    {
        var b = new PatchBuilder(Catalog);
        var clock = b.Add("time");
        var grain = b.Add("math.mul", (1, 3571f));
        var hash = b.Add("math.sin");
        var scatter = b.Add("math.mul", (1, 4371.3f));
        var white = b.Add("math.fract");
        var hiss = b.Add("math.remap", (1, 0f), (2, 1f), (3, -1f), (4, 1f));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(clock, 0, grain, 0).Wire(grain, 0, hash, 0).Wire(hash, 0, scatter, 0)
         .Wire(scatter, 0, white, 0).Wire(white, 0, hiss, 0).Wire(hiss, 0, sink, NodeCatalog.OutputLeftPort);

        Run(b.Patch, 4_000).ShouldBe(Samples().Take(4_000));
    }

    // --- harness -----------------------------------------------------------------

    private static double[] Samples(params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var hiss = b.Add(HissType, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(hiss, 0, sink, NodeCatalog.OutputLeftPort);

        return Run(b.Patch, Length);
    }

    private static double[] Run(Patch patch, int length)
    {
        var program = patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();
        var output = new double[length];

        for (var i = 0; i < length; i++)
        {
            program.Evaluate(0f, 0f, 3d + i / (double)Rate, registers, default);
            output[i] = registers[program.OutputBase];
        }

        return output;
    }

    private static double Correlation(double[] signal, int lag)
    {
        var mean = signal.Average();
        var power = signal.Sum(s => (s - mean) * (s - mean));
        var cross = 0d;

        for (var i = lag; i < signal.Length; i++)
            cross += (signal[i] - mean) * (signal[i - lag] - mean);

        return cross / power;
    }
}
