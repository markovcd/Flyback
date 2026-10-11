using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>The Dice: a stable random for a whole number, in whole-number arithmetic modulo 997.</summary>
public class DiceTests
{
    private const string Dice = "math.dice";

    /// <summary>The hash Dodge wrote by hand for its gaps, which a Dice of seed nought is.</summary>
    private const string Hashed = "mod(mod(mod(floor(a), 997) * mod(floor(a), 997), 997) * 73 + mod(floor(a), 997) * 151 + 17, 997)";

    [Fact]
    public void It_is_a_maths_module_with_a_value_and_a_face()
    {
        var def = NodeCatalog.BuiltIn.Require(Dice);

        def.Name.ShouldBe("Dice");
        def.Category.ShouldBe(ModuleCategories.Maths);
        def.Inputs.Select(p => p.Name).ShouldBe(["in", "seed", "sides"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["out", "face"]);
    }

    [Fact]
    public void Seed_nought_is_the_hash_dodge_wrote()
    {
        Thrown(0, (1, 0f)).ShouldBe(Swept($"{Hashed} / 997"));
        Thrown(1, (2, 7f)).ShouldBe(Swept($"min(floor({Hashed} * 7 / 997), 6)"));
    }

    [Fact]
    public void A_formula_reads_its_value()
    {
        Swept("floor(dice(a) * 7)").ShouldBe(Swept($"min(floor({Hashed} * 7 / 997), 6)"));
    }

    [Fact]
    public void It_throws_the_same_for_every_value_that_rounds_down_to_one_number()
    {
        var thrown = Thrown(0);

        for (var i = 0; i < Steps; i++)
            thrown[i].ShouldBe(thrown[i / PerWhole * PerWhole]);
    }

    [Fact]
    public void Its_faces_are_whole_numbers_under_sides_and_each_comes_up()
    {
        var faces = Thrown(1, (2, 5f)).Distinct().Order().ToArray();

        faces.ShouldBe([0d, 1d, 2d, 3d, 4d]);
    }

    [Fact]
    public void Another_seed_is_another_sequence()
    {
        var one = Thrown(0, (1, 1f));
        var other = Thrown(0, (1, 2f));

        one.Zip(other).Count(p => p.First == p.Second).ShouldBeLessThan(Steps / 10);

        // A quadratic modulo a prime takes half its values, (997 + 1) / 2, over a cycle.
        one.Distinct().Count().ShouldBe(499);
    }

    // --- harness -----------------------------------------------------------------

    private const int Wholes = 1_100, PerWhole = 2, Steps = Wholes * PerWhole;

    private static double[] Thrown(int output, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var dice = b.Add(Dice, knobs);
        b.Wire(b.Add("coord"), 0, dice, 0);

        return Swept(b, dice, output);
    }

    private static double[] Swept(string formula)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = b.Add(NodeCatalog.ExpressionTypeId);
        expression.SetState("expression", new JsonObject { ["formula"] = formula });
        b.Wire(b.Add("coord"), 0, expression, 0);

        return Swept(b, expression, 0);
    }

    /// <summary>One output while x climbs a half at a time from -20, through nought, across more than a whole cycle of 997.</summary>
    private static double[] Swept(PatchBuilder b, NodeInstance module, int output)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(module, output, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program.DelayLengths, 48_000, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var heard = new double[Steps];

        for (var i = 0; i < heard.Length; i++)
        {
            program.Evaluate(-20d + (double)i / PerWhole, 0d, 0d, registers, default, state);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }
}
