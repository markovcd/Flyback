using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>The Window, which is two Thresholds or two Smoothsteps and a Multiply.</summary>
public class WindowTests
{
    private const string Window = "math.window";

    [Fact]
    public void It_is_a_maths_module_a_formula_can_call()
    {
        var def = NodeCatalog.BuiltIn.Require(Window);

        def.Name.ShouldBe("Window");
        def.Category.ShouldBe(ModuleCategories.Maths);
        def.Inputs.Select(p => p.Name).ShouldBe(["in", "from", "to", "rise", "fall"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["out"]);
    }

    [Theory]
    [InlineData(-0.5f, 11.5f)]
    [InlineData(-0.91f, 0.91f)]
    [InlineData(0.875f, 1f)]
    public void A_hard_window_is_a_pair_of_thresholds(float from, float to)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var window = b.Add(Window, (1, from), (2, to));
        b.Wire(b.Add("coord"), 0, window, 0);

        Swept(b, window).ShouldBe(Swept($"step({F(from)}, a) * (1 - step({F(to)}, a))"));
    }

    [Fact]
    public void A_soft_window_ramps_inside_its_ends()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var window = b.Add(Window, (1, 0.5f), (2, 6f), (3, 0.25f), (4, 1.5f));
        b.Wire(b.Add("coord"), 0, window, 0);

        Swept(b, window).ShouldBe(Swept("smoothstep(0.5, 0.75, a) * (1 - smoothstep(4.5, 6, a))"));
    }

    [Fact]
    public void A_formula_calls_it_by_name()
    {
        Swept("window(a, -0.5, 11.5)").ShouldBe(Swept("step(-0.5, a) * (1 - step(11.5, a))"));
    }

    [Fact]
    public void It_is_on_between_its_ends_and_off_outside()
    {
        var heard = Swept("window(a, 2, 5)");

        heard.Distinct().Order().ShouldBe([0d, 1d]);
        heard.Count(v => v == 1d).ShouldBeInRange(Steps * 3 / 16 - 2, Steps * 3 / 16 + 2);
    }

    // --- harness -----------------------------------------------------------------

    private const int Steps = 1_600;

    private static string F(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static double[] Swept(string formula)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(NodeCatalog.ExpressionTypeId);
        expression.SetState("expression", new JsonObject { ["formula"] = formula });
        b.Wire(b.Add("coord"), 0, expression, 0);

        return Swept(b, expression);
    }

    /// <summary>One module's output while x runs from -2 to 14, the one input a test can vary a sample at a time.</summary>
    private static double[] Swept(PatchBuilder b, NodeInstance module)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(module, 0, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program.DelayLengths, 48_000, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var heard = new double[Steps];

        for (var i = 0; i < heard.Length; i++)
        {
            program.Evaluate(-2d + 16d * i / Steps, 0d, 0d, registers, default, state);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }
}
