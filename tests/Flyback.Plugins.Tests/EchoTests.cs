using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Echo, run a sample at a time on a click. It is two delay lines, so it needs
/// the state a renderer would give it.
/// </summary>
public class EchoTests
{
    private const string EchoType = "flyback.effects.echo";

    private const int TempoPort = 1;
    private const int LeftPort = 2;
    private const int RightPort = 3;
    private const int FeedbackPort = 4;
    private const int MixPort = 5;

    private const int Rate = 48_000;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_effects_plugin_offers_it_beside_the_delay()
    {
        var def = Catalog.Get(EchoType).ShouldNotBeNull();

        def.Name.ShouldBe("Echo");
        def.Category.ShouldBe(ModuleCategories.TimeEffects);
        def.Outputs.Select(p => p.Name).ShouldBe(["left", "right"]);
        def.Extra<SettingsExtra>().ShouldNotBeNull().Fields.Select(f => f.Key).ShouldBe(["taps", "division"]);
        Catalog.ProviderOf(EchoType)!.Id.ShouldBe("flyback.effects");
    }

    [Fact]
    public void Three_steps_at_two_beats_a_second_is_three_eighths_of_a_second()
    {
        var heard = Played("row", 0, (TempoPort, 2f), (LeftPort, 3f), (FeedbackPort, 0f), (MixPort, 1f));

        Loudest(heard).ShouldBeInRange(Rate * 3 / 8 - 2, Rate * 3 / 8 + 2);
    }

    [Fact]
    public void In_a_row_the_right_tap_comes_after_the_left_and_side_by_side_it_does_not()
    {
        var knobs = new[] { (TempoPort, 2f), (LeftPort, 3f), (RightPort, 2f), (FeedbackPort, 0f), (MixPort, 1f) };

        Loudest(Played("row", 1, knobs)).ShouldBeInRange(Rate * 5 / 8 - 2, Rate * 5 / 8 + 2);
        Loudest(Played("side", 1, knobs)).ShouldBeInRange(Rate * 2 / 8 - 2, Rate * 2 / 8 + 2);
    }

    /// <summary>
    /// A Multiply, two Divides and two Delays wired the way every track wires them,
    /// against this. Equal rather than close, on both sides and both ways round.
    /// </summary>
    [Theory]
    [InlineData("row", 0)]
    [InlineData("row", 1)]
    [InlineData("side", 0)]
    [InlineData("side", 1)]
    public void It_is_the_modules_it_stands_for_to_the_last_bit(string taps, int side)
    {
        var inARow = taps == "row";

        var b = new PatchBuilder(Catalog);
        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 112f));
        var sixteenths = b.Add("math.mul", (1, 4f));
        var dotted = b.Add("math.div", (0, 3f));
        var straight = b.Add("math.div", (0, 2f));
        var tapL = b.Add(NodeCatalog.DelayTypeId, (2, 0.45f), (3, 0.7f));
        var tapR = b.Add(NodeCatalog.DelayTypeId, (2, inARow ? 0f : 0.45f), (3, 0.7f));
        var click = Click(b);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(tempo, 0, sixteenths, 0)
         .Wire(sixteenths, 0, dotted, 1).Wire(sixteenths, 0, straight, 1)
         .Wire(click, 0, tapL, 0).Wire(dotted, 0, tapL, 1)
         .Wire(inARow ? tapL : click, 0, tapR, 0).Wire(straight, 0, tapR, 1)
         .Wire(side == 0 ? tapL : tapR, 0, sink, NodeCatalog.OutputLeftPort);

        var wrapped = new PatchBuilder(Catalog);
        var echo = Echo(wrapped, taps, (FeedbackPort, 0.45f), (MixPort, 0.7f));
        var sink2 = wrapped.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        wrapped.Wire(wrapped.Add(NodeCatalog.TempoTypeId, (0, 112f)), 0, echo, TempoPort)
               .Wire(echo, side, sink2, NodeCatalog.OutputLeftPort);

        Run(wrapped.Patch).ShouldBe(Run(b.Patch));
    }

    // --- harness -----------------------------------------------------------------

    private static double[] Played(string taps, int side, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var echo = Echo(b, taps, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(echo, side, sink, NodeCatalog.OutputLeftPort);

        return Run(b.Patch);
    }

    private static NodeInstance Echo(PatchBuilder b, string taps, params (int Port, float Value)[] knobs)
    {
        var echo = b.Add(EchoType, knobs);
        echo.SetState("echo", new JsonObject { ["taps"] = taps });
        b.Wire(Click(b), 0, echo, 0);
        return echo;
    }

    /// <summary>One for the first hundredth of a second and nothing after.</summary>
    private static NodeInstance Click(PatchBuilder b)
    {
        var clock = b.Add("time");
        var early = b.Add("math.step", (0, 0.01f));
        var click = b.Add("math.sub", (0, 1f));

        b.Wire(clock, 0, early, 1).Wire(early, 0, click, 1);

        return click;
    }

    private static double[] Run(Patch patch)
    {
        var result = patch.CompileForAudio(Catalog);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var output = new double[Rate];

        for (var i = 0; i < output.Length; i++)
        {
            program.Evaluate(0f, 0f, i / (double)Rate, registers, default, state);
            output[i] = registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>Where the loudest sample after the click itself is.</summary>
    private static int Loudest(double[] heard)
    {
        var skip = Rate / 50;
        var loudest = skip;

        for (var i = skip; i < heard.Length; i++)
            if (Math.Abs(heard[i]) > Math.Abs(heard[loudest])) loudest = i;

        return loudest;
    }
}
