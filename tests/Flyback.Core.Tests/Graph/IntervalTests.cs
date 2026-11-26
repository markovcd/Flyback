using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>The Interval module, and a lag written in a pack that turns hertz with it.</summary>
public class IntervalTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private const string Lag = """
        pack ada.lag 1.0.0

        /// A one-pole lowpass built from a cycle.
        module lag(in = 0, hz = 10) = {
          let clock = interval()
          let k = 1 - exp(-6.2831853 * hz * clock.seconds)
          let held = add(a: 0)
          let next = math.mix(in, held + (in - held) * k, clock.remembers)
          held.b <- next
          next
        }
        """;

    [Fact]
    public void It_is_offered_under_sources_for_audio()
    {
        var def = NodeCatalog.BuiltIn.Get(IntervalModule.TypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Interval");
        def.Category.ShouldBe(ModuleCategories.Sources);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        def.Outputs.Select(p => p.Name).ShouldBe(["seconds", "remembers"]);
    }

    [Fact]
    public void On_the_sound_it_remembers_from_the_second_sample_one_sample_apart()
    {
        var remembers = Heard("interval().remembers |> out.left", NodeCatalog.BuiltIn, _ => 0f, 4);
        var seconds = Heard("interval().seconds |> out.left", NodeCatalog.BuiltIn, _ => 0f, 4);

        remembers.ShouldBe([0f, 1f, 1f, 1f]);
        seconds[1..].ShouldAllBe(s => Math.Abs(s - 1f / Rate) < 1e-9f);
    }

    [Fact]
    public void On_the_picture_it_remembers_nothing()
    {
        var program = Built("interval().remembers |> out.color", NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0.5f, 0f, 1f, registers, default);
        program.Evaluate(0.5f, 0f, 1f, registers, default);

        ((float)registers[program.OutputBase]).ShouldBe(0f);
    }

    [Fact]
    public void A_pack_lag_closes_the_gap_at_the_rate_it_is_set_to()
    {
        var catalog = Packed();
        var output = Heard("ada.lag.lag(in: x, hz: 10) |> out.left", catalog, i => i < Rate / 4 ? 0f : 1f, Rate / 2);

        // One time constant, 1 / (2 pi 10) seconds after the step, is 63% of the way.
        output[Rate / 4 + (int)(Rate / (2 * Math.PI * 10))].ShouldBe(1f - MathF.Exp(-1f), 0.01f);
        output[^1].ShouldBe(1f, 0.01f);
    }

    [Fact]
    public void A_pack_lag_starts_at_its_input()
    {
        Heard("ada.lag.lag(in: x) |> out.left", Packed(), _ => 0.8f, 2)[0].ShouldBe(0.8f, 1e-6f);
    }

    [Fact]
    public void A_pack_lag_is_a_wire_on_the_picture()
    {
        var catalog = Packed();
        var program = Built("ada.lag.lag(in: x) |> out.color", catalog).CompileForVideo(catalog).Program;
        var registers = program.AllocateRegisters();

        foreach (var x in new[] { -0.75f, 0f, 0.5f })
        {
            program.Evaluate(x, 0f, 0f, registers, default);
            ((float)registers[program.OutputBase]).ShouldBe(x, 1e-6f);
        }
    }

    private static ModuleCatalog Packed()
    {
        var (catalog, issues) = PackModules.Into(NodeCatalog.BuiltIn, Lag);

        issues.ShouldBeEmpty(string.Join("; ", issues.Select(i => $"{i.Line}: {i.Message}")));

        return catalog;
    }

    private static Patch Built(string source, ModuleCatalog catalog)
    {
        var load = PatchLanguage.Build(source + "\nout.volume = 1\n", catalog);

        load.Issues.Where(i => i.IsError).ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static float[] Heard(string source, ModuleCatalog catalog, Func<int, float> x, int length)
    {
        var program = Built(source, catalog).CompileForAudio(catalog).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();
        var output = new float[length];

        for (var i = 0; i < length; i++)
        {
            program.Evaluate(x(i), 0f, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }
}
