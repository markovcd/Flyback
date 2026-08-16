using CsCheck;
using Shouldly;
using Flyback.Core.Compile;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// ADR-0013 promises that a half-built patch never blacks the screen out with a
/// NaN. These pin that promise op by op, over generated inputs rather than
/// chosen ones — the degenerate cases (exact zero, negatives) are mixed into the
/// generator explicitly, because random floats essentially never land on 0.
/// </summary>
public class InterpreterInvariants
{
    /// <summary>
    /// Deliberately bounded. ADR-0013 guards degenerate *operations* — division
    /// by zero, the log of a negative — not overflow from astronomical operands:
    /// nothing guards `Mul`, so 1e38 * 1e38 is legitimately infinite. The
    /// end-to-end guarantee against that lives in the renderer's Saturate, which
    /// <see cref="PatchFuzzTests"/> covers.
    /// </summary>
    private static readonly Gen<float> Operand =
        Gen.OneOf(
            Gen.Const(0f),
            Gen.Const(-0f),
            Gen.Const(1f),
            Gen.Const(-1f),
            Gen.Float[-1000f, 1000f]);

    private static readonly OpCode[] UnaryOps =
    [
        OpCode.Neg, OpCode.Abs, OpCode.Sin, OpCode.Cos, OpCode.Tan, OpCode.Sqrt,
        OpCode.Floor, OpCode.Ceil, OpCode.Fract, OpCode.Sign, OpCode.Exp, OpCode.Log,
    ];

    private static readonly OpCode[] BinaryOps =
    [
        OpCode.Add, OpCode.Sub, OpCode.Mul, OpCode.Div, OpCode.Mod, OpCode.Pow,
        OpCode.Min, OpCode.Max, OpCode.Atan2, OpCode.Step, OpCode.Hypot,
    ];

    private static readonly OpCode[] TernaryOps =
    [
        OpCode.Clamp, OpCode.Mix, OpCode.Smoothstep, OpCode.Noise3,
    ];

    public static TheoryData<OpCode> Unary => [.. UnaryOps];

    public static TheoryData<OpCode> Binary => [.. BinaryOps];

    public static TheoryData<OpCode> Ternary => [.. TernaryOps];

    [Theory]
    [MemberData(nameof(Unary))]
    public void Unary_ops_never_produce_a_non_finite_value(OpCode code)
    {
        var machine = Machine.Unary(code);

        Operand.Sample(x =>
            double.IsFinite(machine.Run(x, 0f)).ShouldBeTrue($"{code}({x}) was not finite"));
    }

    [Theory]
    [MemberData(nameof(Binary))]
    public void Binary_ops_never_produce_a_non_finite_value(OpCode code)
    {
        var machine = Machine.Binary(code);

        Gen.Select(Operand, Operand).Sample((x, y) =>
            double.IsFinite(machine.Run(x, y)).ShouldBeTrue($"{code}({x}, {y}) was not finite"));
    }

    [Theory]
    [MemberData(nameof(Ternary))]
    public void Ternary_ops_never_produce_a_non_finite_value(OpCode code)
    {
        var machine = Machine.Ternary(code);

        Gen.Select(Operand, Operand, Operand).Sample((x, y, t) =>
            double.IsFinite(machine.Run(x, y, t)).ShouldBeTrue($"{code}({x}, {y}, {t}) was not finite"));
    }

    [Fact]
    public void Dividing_by_zero_yields_zero()
    {
        var machine = Machine.Binary(OpCode.Div);

        Operand.Sample(x => machine.Run(x, 0f).ShouldBe(0d));
    }

    [Fact]
    public void Modulo_by_zero_yields_zero()
    {
        var machine = Machine.Binary(OpCode.Mod);

        Operand.Sample(x => machine.Run(x, 0f).ShouldBe(0d));
    }

    [Fact]
    public void The_square_root_of_a_non_positive_number_is_zero()
    {
        var machine = Machine.Unary(OpCode.Sqrt);

        Gen.Float[-1000f, 0f].Sample(x => machine.Run(x, 0f).ShouldBe(0d));
    }

    [Fact]
    public void The_log_of_a_non_positive_number_is_zero()
    {
        var machine = Machine.Unary(OpCode.Log);

        Gen.Float[-1000f, 0f].Sample(x => machine.Run(x, 0f).ShouldBe(0d));
    }

    /// <summary>Fract wraps into 0..1, which is what every tiling module relies on.</summary>
    [Fact]
    public void Fract_always_lands_between_zero_inclusive_and_one_exclusive()
    {
        var machine = Machine.Unary(OpCode.Fract);

        Gen.Float[-1000f, 1000f].Sample(x =>
        {
            var result = machine.Run(x, 0f);
            result.ShouldBeGreaterThanOrEqualTo(0d);
            result.ShouldBeLessThan(1d);
        });
    }

    /// <summary>Value noise is documented as 0..1; the colour modules assume it.</summary>
    [Fact]
    public void Value_noise_stays_within_zero_to_one()
    {
        var machine = Machine.Ternary(OpCode.Noise3);

        Gen.Select(Gen.Float[-500f, 500f], Gen.Float[-500f, 500f], Gen.Float[-500f, 500f])
            .Sample((x, y, z) => machine.Run(x, y, z).ShouldBeInRange(0d, 1d));
    }

    /// <summary>
    /// ADR-0007: a scalar broadcasts across all three components, so scaling a
    /// colour by 1.0 must be the identity rather than a narrowing.
    /// </summary>
    [Fact]
    public void Multiplying_a_colour_by_one_leaves_it_unchanged()
    {
        var emitter = new Emitter();
        var r = emitter.Load(OpCode.LoadX);
        var g = emitter.Load(OpCode.LoadY);
        var b = emitter.Load(OpCode.LoadT);
        var colour = emitter.Combine(r, g, b);
        var scaled = emitter.Binary(OpCode.Mul, colour, emitter.Constant(1f));

        scaled.Width.ShouldBe(3);

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, scaled.Base);

        Gen.Select(Operand, Operand, Operand).Sample((x, y, t) =>
        {
            var registers = program.AllocateRegisters();
            program.Evaluate(x, y, t, registers, default);

            registers[scaled.Base + 0].ShouldBe(x);
            registers[scaled.Base + 1].ShouldBe(y);
            registers[scaled.Base + 2].ShouldBe(t);
        });
    }

    /// <summary>
    /// A one-op program over the machine's own public surface. Built once per
    /// opcode and reused across samples, so the generator drives evaluation
    /// rather than compilation.
    /// </summary>
    private sealed class Machine
    {
        private readonly CompiledPatch program;
        private readonly int result;

        private Machine(CompiledPatch program, int result)
        {
            this.program = program;
            this.result = result;
        }

        public static Machine Unary(OpCode code) => Build(e => e.Unary(code, X(e)));

        public static Machine Binary(OpCode code) => Build(e => e.Binary(code, X(e), Y(e)));

        public static Machine Ternary(OpCode code) => Build(e => e.Ternary(code, X(e), Y(e), T(e)));

        /// <summary>
        /// Register scratch is allocated per call, not cached on the instance:
        /// CsCheck samples in parallel, and a shared register file would let
        /// concurrent samples overwrite each other's intermediates.
        /// </summary>
        public double Run(float x, float y, float t = 0f)
        {
            var registers = program.AllocateRegisters();
            program.Evaluate(x, y, t, registers, default);
            return registers[result];
        }

        private static Slot X(Emitter e) => e.Load(OpCode.LoadX);

        private static Slot Y(Emitter e) => e.Load(OpCode.LoadY);

        private static Slot T(Emitter e) => e.Load(OpCode.LoadT);

        private static Machine Build(Func<Emitter, Slot> emit)
        {
            var emitter = new Emitter();
            var result = emit(emitter);
            return new Machine(
                new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, result.Base),
                result.Base);
        }
    }
}
