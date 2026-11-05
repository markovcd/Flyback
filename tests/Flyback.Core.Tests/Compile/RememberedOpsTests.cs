using Flyback.Core.Compile;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// The ops the whole program's IL remembers between evaluations, which have to answer
/// exactly what they would have worked out afresh.
/// </summary>
public class RememberedOpsTests
{
    [Fact]
    public void A_remembered_power_tells_a_negative_nought_from_a_positive_one()
    {
        var memory = IlOps.Memory([OpCode.Pow]);

        Bits(IlOps.RememberedPow(memory, 0, 0d, 3d)).ShouldBe(Bits(0d));
        Bits(IlOps.RememberedPow(memory, 0, -0d, 3d)).ShouldBe(Bits(-0d));
    }

    [Fact]
    public void Remembered_answers_are_the_ones_worked_out_afresh()
    {
        var memory = IlOps.Memory([OpCode.Exp, OpCode.Pow, OpCode.Noise3]);
        var random = new Random(7);

        for (var i = 0; i < 5_000; i++)
        {
            // Mostly the same question again, as a sound asks it, with a new one now and then.
            var a = i % 7 == 0 ? random.NextDouble() * 8d - 4d : Math.Round(i / 40d) * 0.3d;
            var b = i % 11 == 0 ? double.NaN : 2.5d - (i % 3);
            var c = i % 13 == 0 ? double.PositiveInfinity : i * 0.01d;

            Bits(IlOps.RememberedExp(memory, 0, a)).ShouldBe(Bits(IlOps.Exp(a)));
            Bits(IlOps.RememberedPow(memory, 3, a, b)).ShouldBe(Bits(IlOps.Pow(a, b)));
            Bits(IlOps.RememberedNoise3(memory, 6, a, b * 0.5d, c)).ShouldBe(Bits(Noise.Value3(a, b * 0.5d, c)));
        }
    }

    [Fact]
    public void A_fresh_memory_already_holds_true_answers()
    {
        var memory = IlOps.Memory([OpCode.Pow, OpCode.Exp, OpCode.Noise3]);

        IlOps.RememberedPow(memory, 0, 0d, 0d).ShouldBe(IlOps.Pow(0d, 0d));
        IlOps.RememberedExp(memory, 3, 0d).ShouldBe(IlOps.Exp(0d));
        IlOps.RememberedNoise3(memory, 6, 0.25d, 0.5d, 0.75d).ShouldBe(Noise.Value3(0.25d, 0.5d, 0.75d));
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
