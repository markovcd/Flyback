using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// ADR-0013's rule, op by op: every instruction answers something for every
/// register it could be handed, rather than raising or propagating a NaN.
/// </summary>
/// <remarks>
/// One op at a time, fed constants, where <see cref="IlProgramTests"/> runs whole
/// presets: a preset only reaches the values a preset reaches, and the ones that
/// break a guard are the ones no patch was written to produce. The IL is checked
/// against the interpreter here as well as there, because a guard the two disagree
/// about is exactly what this feeds them.
/// </remarks>
public class TotalityTests
{
    public static TheoryData<OpCode> AllOpCodes => [.. Enum.GetValues<OpCode>()];

    /// <summary>What a register can hold, including everything a guard is there for.</summary>
    private static readonly float[] Hostile =
    [
        0f, -0f, 1f, -1f, 0.5f, -0.5f, 2f, -2f, 3f, 0.1f, -0.1f,
        float.Epsilon, -float.Epsilon, 1e20f, -1e20f, 1e-20f,
        float.MaxValue, float.MinValue,
        float.PositiveInfinity, float.NegativeInfinity, float.NaN,
    ];

    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void Every_op_answers_every_input_without_throwing(OpCode code)
    {
        var failures = new List<string>();

        foreach (var a in Hostile)
        foreach (var b in Hostile)
        {
            var program = Single(code, a, b);
            var registers = program.AllocateRegisters();

            try
            {
                program.Evaluate(0.25d, -0.5d, 3d, registers, default);
            }
            catch (Exception e)
            {
                failures.Add($"{code}({a}, {b}) threw {e.GetType().Name}: {e.Message}");
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures.Take(5)));
    }

    [Theory]
    [MemberData(nameof(AllOpCodes))]
    public void The_il_is_the_interpreter_on_every_input(OpCode code)
    {
        var failures = new List<string>();

        foreach (var a in Hostile)
        foreach (var b in Hostile)
        {
            var program = Single(code, a, b);
            var il = IlProgram.Compile(program, IlParts.Whole);

            var expected = program.AllocateRegisters();
            var actual = program.AllocateRegisters();

            program.Evaluate(0.25d, -0.5d, 3d, expected, default);
            il.Evaluate(0.25d, -0.5d, 3d, actual, default);

            for (var i = 0; i < program.OutputWidth; i++)
                if (BitConverter.DoubleToInt64Bits(expected[program.OutputBase + i])
                    != BitConverter.DoubleToInt64Bits(actual[program.OutputBase + i]))
                    failures.Add(
                        $"{code}({a}, {b})[{i}]: interpreter {expected[program.OutputBase + i]}, "
                        + $"il {actual[program.OutputBase + i]}");
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// One op fed constants, its result the program's output. <c>c</c> is
    /// <paramref name="b"/>, since a third axis over the same list would be
    /// twenty-one times the cases for the same guards.
    /// </summary>
    private static CompiledPatch Single(OpCode code, float a, float b)
    {
        var ops = new List<Op>
        {
            new(OpCode.Const, 0, k: a),
            new(OpCode.Const, 1, k: b),
            new(OpCode.Const, 2, k: b),
        };

        var width = Outputs(code);

        // K is a slot, a clip or a length depending on the op, and only the
        // arithmetic ops read it as a value.
        var k = code switch
        {
            OpCode.Delay or OpCode.Allpass => 0.25f,
            OpCode.Const => a,
            _ => 0f,
        };

        ops.Add(new Op(code, 3, a: 0, b: 1, c: 2, k: k));

        // An op that writes nothing still has to leave the program an output.
        if (width == 0) ops.Add(new Op(OpCode.Copy, 3, a: 0));

        return new CompiledPatch([.. ops], registerCount: 8, outputBase: 3, outputWidth: Math.Max(width, 1));
    }

    private static int Outputs(OpCode code) => code switch
    {
        OpCode.Tap or OpCode.UnitWrite or OpCode.PlaneWrite or OpCode.ClockWrite => 0,
        OpCode.HsvToRgb or OpCode.SampleFeedback or OpCode.SamplePicture => 3,
        _ => 1,
    };
}
