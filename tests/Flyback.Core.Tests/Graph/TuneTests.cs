using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Tune, which is an Add, a Quantiser and a Note, and the Tempo's count of beats,
/// which is a Time and a Multiply.
/// </summary>
public class TuneTests
{
    private const string Tune = "audio.tune";

    private const int Rate = 48_000;

    private static readonly int[] Minor = [9, 11, 0, 2, 4, 5, 8];

    [Fact]
    public void It_is_a_pitch_module_that_carries_a_scale()
    {
        var def = NodeCatalog.BuiltIn.Require(Tune);

        def.Name.ShouldBe("Tune");
        def.Category.ShouldBe(ModuleCategories.Pitch);
        def.Inputs.Select(p => p.Name).ShouldBe(["in", "transpose", "hold"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["hz", "note"]);
        def.Extra<ScaleExtra>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void It_is_an_add_into_a_quantiser_into_a_note(int output)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var line = b.Add("coord");
        var moved = b.Add("math.add", (1, 12f));
        var snap = b.Add(NodeCatalog.QuantiserTypeId);
        ScaleExtra.Set(snap, Minor);
        var note = b.Add("audio.note");

        b.Wire(line, 0, moved, 0).Wire(moved, 0, snap, 0).Wire(snap, 0, note, 0);

        var wrapped = new PatchBuilder(NodeCatalog.BuiltIn);
        var tune = wrapped.Add(Tune, (1, 12f));
        ScaleExtra.Set(tune, Minor);
        wrapped.Wire(wrapped.Add("coord"), 0, tune, 0);

        Played(wrapped, tune, output).ShouldBe(Played(b, note, output));
    }

    [Fact]
    public void A_wire_into_transpose_moves_the_line_onto_the_chord()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var tune = b.Add(Tune, (0, 45f));
        ScaleExtra.Set(tune, Minor);
        b.Wire(b.Add("coord"), 0, tune, 1);

        var notes = Played(b, tune, 1);

        notes.ShouldAllBe(n => Minor.Contains((int)n % 12));
        notes.Min().ShouldBe(89d);
        notes.Distinct().Count().ShouldBeGreaterThan(10);
    }

    [Fact]
    public void A_tempo_counts_beats_as_a_time_and_a_multiply_do()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var tempo = b.Add(NodeCatalog.TempoTypeId, (0, 112f));
        var clock = b.Add(NodeCatalog.TimeTypeId);
        var beats = b.Add("math.mul");
        b.Wire(clock, 0, beats, 0).Wire(tempo, 0, beats, 1);

        var wrapped = new PatchBuilder(NodeCatalog.BuiltIn);
        var counted = wrapped.Add(NodeCatalog.TempoTypeId, (0, 112f));

        NodeCatalog.BuiltIn.Require(NodeCatalog.TempoTypeId).Outputs.Select(p => p.Name).ShouldBe(["out", "beats"]);
        Played(wrapped, counted, 1).ShouldBe(Played(b, beats, 0));
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>
    /// One output of a module, heard over a run in which x climbs two octaves from
    /// A2 — the one input a test can vary a sample at a time.
    /// </summary>
    private static double[] Played(PatchBuilder b, NodeInstance module, int output)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(module, output, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var heard = new double[2_400];

        for (var i = 0; i < heard.Length; i++)
        {
            program.Evaluate(45d + 24d * i / heard.Length, 0d, i / (double)Rate, registers, default, state);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }
}
