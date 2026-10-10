using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>The Triangle: a straight line down and back up each cycle, worked out by hand.</summary>
public class TriangleTests
{
    // Sockets on the module, named so a shifted one fails here and not as a silent change of meaning.
    private const int Frequency = 1;
    private const int Phase = 2;
    private const int Amp = 3;
    private const int Bias = 4;

    /// <summary>
    /// The wave at <paramref name="phase"/> cycles in. With the frequency at zero the input
    /// contributes nothing, and a stateless evaluation reads the phase knob as it stands.
    /// </summary>
    private static double At(float phase, float amp = 1f, float bias = 0f)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var wave = builder.Add(NodeCatalog.TriangleTypeId, 0, 0, (Frequency, 0f), (Phase, phase), (Amp, amp), (Bias, bias));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(wave, 0, sink, NodeCatalog.OutputLeftPort);

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0f, 0f, 0f, registers, default);

        return registers[program.OutputBase];
    }

    /// <summary>Peak at the start of the cycle, zero a quarter in, trough halfway, and back.</summary>
    [Theory]
    [InlineData(0f, 1.0)]
    [InlineData(0.125f, 0.5)]
    [InlineData(0.25f, 0.0)]
    [InlineData(0.5f, -1.0)]
    [InlineData(0.75f, 0.0)]
    [InlineData(0.875f, 0.5)]
    public void It_runs_straight_between_its_peak_and_its_trough(float phase, double expected) =>
        At(phase).ShouldBe(expected);

    [Fact]
    public void A_whole_cycle_more_lands_where_it_started() =>
        At(1.25f).ShouldBe(At(0.25f));

    [Fact]
    public void Amp_scales_it_and_bias_lifts_it() =>
        At(0f, amp: 0.5f, bias: 0.25f).ShouldBe(0.75);
}
