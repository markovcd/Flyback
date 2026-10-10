using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The ADSR. Unlike the sequencers beside it this one has a memory, so what it
/// hands out is a function of every evaluation before it — which means these run
/// it a step at a time and read the shape off the run.
/// </summary>
/// <remarks>
/// The gate is fed from x rather than a knob so one compiled program can be opened
/// and closed without recompiling, and the state is a <see cref="DelayState"/>
/// handed to every evaluation — the one the audio path passes and the video path
/// does not.
/// </remarks>
public class TempoTests
{
    /// <summary>
    /// The one module that knows a tempo is written in beats a minute while
    /// everything downstream of it counts in beats a second. Nothing else in the
    /// catalog would let 120 be typed as 120.
    /// </summary>
    [Theory]
    [InlineData(120f, 2f)]
    [InlineData(60f, 1f)]
    [InlineData(90f, 1.5f)]
    [InlineData(174f, 2.9f)]
    public void A_tempo_in_beats_a_minute_comes_out_in_beats_a_second(float bpm, float expected)
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.TempoTypeId);
        var emitter = new Emitter();

        var outputs = def.Emit(emitter, new EmitContext([emitter.Constant(bpm), emitter.Constant(0f)]));
        var program = new CompiledPatch(
            emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1);

        var registers = program.AllocateRegisters();
        program.Evaluate(0, 0, 0, registers, default);

        ((float)registers[outputs[0].Base]).ShouldBe(expected, 1e-5f);
    }

    /// <summary>The knob it opens on, which is what the Kick preset is set to.</summary>
    [Fact]
    public void It_opens_at_a_hundred_and_twenty() =>
        NodeCatalog.BuiltIn.Require(NodeCatalog.TempoTypeId).Inputs[0].Default.ShouldBe(120f);
}
