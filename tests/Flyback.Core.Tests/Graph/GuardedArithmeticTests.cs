using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Arithmetic with no answer gives zero rather than NaN or an infinity (ADR-0013),
/// and a Clamp whose range is upside down holds at its low end: each module on its
/// own, answered by what it sends on.
/// </summary>
public class GuardedArithmeticTests
{
    [Theory]
    [InlineData("math.div", 1f, 0f)] // infinity
    [InlineData("math.mod", 1f, 0f)] // NaN
    [InlineData("math.pow", -2f, 0.5f)] // NaN, a negative root
    public void A_calculation_with_no_answer_gives_zero(string typeId, float a, float b) =>
        Sent(typeId, ("a", a), ("b", b)).ShouldBe(0d);

    [Theory]
    [InlineData("math.sqrt", -1f)] // NaN
    [InlineData("math.log", 0f)] // negative infinity
    [InlineData("math.exp", 1000f)] // infinity: exp overflows a double past about 710
    public void A_function_outside_its_domain_gives_zero(string typeId, float input) =>
        Sent(typeId, ("in", input)).ShouldBe(0d);

    /// <summary>Holding at the low end is arbitrary, but it is a number, and nobody mid-drag on the knob can afford an exception.</summary>
    [Fact]
    public void A_clamp_whose_range_is_upside_down_holds_at_its_low_end() =>
        Sent("math.clamp", ("in", 0.75f), ("low", 0.25f), ("high", -1f)).ShouldBe(0.25d, 1e-6);

    /// <summary>What <paramref name="typeId"/>, its knobs set, sends to the screen.</summary>
    private static double Sent(string typeId, params (string Socket, float Value)[] knobs)
    {
        var def = NodeCatalog.BuiltIn.Require(typeId);
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var maths = b.Add(typeId, [.. knobs.Select(k => (def.Inputs.ToList().FindIndex(p => p.Name == k.Socket), k.Value))]);
        b.Wire(maths, 0, b.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, registers, default);

        return registers[program.OutputBase];
    }
}
