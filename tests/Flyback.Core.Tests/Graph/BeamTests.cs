using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Beam: an X-Y oscilloscope of what the speakers played, drawn as the trace a
/// beam leaves on phosphor.
/// </summary>
public class BeamTests
{
    private const string Beam = NodeCatalog.BeamTypeId;
    private const string Sine = "osc.sine";
    private const string Square = "osc.square";
    private const string Value = "value";

    private const int Across = 0;
    private const int Up = 1;
    private const int Persistence = 2;
    private const int Intensity = 3;

    private const int Freq = 1;
    private const int Phase = 2;
    private const int Amp = 3;

    /// <summary>How lit the screen is at a place: the phosphor's green.</summary>
    private static double Lit(CompiledPatch program, double x, double y)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, registers, default, aspect: 16d / 9d);

        return registers[program.OutputBase + 1];
    }

    private static int Count(CompiledPatch program, OpCode code) =>
        program.Ops.Count(op => op.Code == code);

    /// <summary>
    /// A Beam on the Output's screen drawing <paramref name="across"/> against
    /// <paramref name="up"/>, with nothing wired to the speakers: what it draws is
    /// played because it is tapped, not because anything hears it.
    /// </summary>
    private static (Patch Patch, NodeInstance Beam) Drawing(
        (string Type, (int Port, float Value)[] Knobs) across,
        (string Type, (int Port, float Value)[] Knobs) up,
        params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var x = b.Add(across.Type, 0, 0, across.Knobs);
        var y = b.Add(up.Type, 0, 200, up.Knobs);
        var beam = b.Add(Beam, 400, 0, knobs);

        b.Wire(x, 0, beam, Across)
         .Wire(y, 0, beam, Up)
         .Wire(beam, 0, output, 0);

        return (b.Patch, beam);
    }

    /// <summary>A circle of radius a half, once round every hundredth of a second.</summary>
    private static (Patch Patch, NodeInstance Beam) Circle(params (int Port, float Value)[] knobs) =>
        Drawing(
            (Sine, [(Freq, 100f), (Amp, 0.5f)]),
            (Sine, [(Freq, 100f), (Phase, 0.25f), (Amp, 0.5f)]),
            knobs);

    /// <summary>Plays the sound for <paramref name="seconds"/> and draws the screen from it.</summary>
    private static CompiledPatch Shown(Patch patch, double seconds = 0.25d)
    {
        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard);
        renderer.Render(heard, new float[(int)(seconds * renderer.SampleRate) * 2], memory);

        Traces.Refresh(drawn, heard, memory);

        return drawn;
    }

    [Fact]
    public void Both_inputs_are_played_though_nothing_hears_them()
    {
        var (patch, beam) = Circle();

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        Count(heard, OpCode.Sin).ShouldBe(2);
        heard.TraceCount.ShouldBe(2);
        heard.Taps.Select(tap => (tap.Node, tap.Port)).ShouldBe([(beam.Id, 0), (beam.Id, 1)]);
    }

    [Fact]
    public void The_screen_reads_what_was_played_rather_than_computing_it()
    {
        var (patch, _) = Circle();

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        Count(drawn, OpCode.Sin).ShouldBe(0);
        drawn.Taps.ShouldHaveSingleItem().Chart.ShouldBe(ChartKind.Beam);
    }

    [Fact]
    public void A_circle_played_is_a_ring_drawn()
    {
        var drawn = Shown(Circle().Patch);

        var ring = new[] { Lit(drawn, 0.5, 0), Lit(drawn, 0, 0.5), Lit(drawn, -0.5, 0), Lit(drawn, 0, -0.5) };

        ring.ShouldAllBe(lit => lit > 0.3);
        ring.Max().ShouldBeLessThan(ring.Min() * 1.2);

        Lit(drawn, 0, 0).ShouldBeLessThan(0.01);
        Lit(drawn, 0.8, 0).ShouldBeLessThan(0.01);
        Lit(drawn, 1.5, 0).ShouldBe(0d);
    }

    /// <summary>
    /// What makes it look like an oscilloscope rather than a plot: the beam lays down
    /// the same energy every moment, so it is brightest where it turns back and
    /// slows, and dimmest where it sweeps fastest through the middle.
    /// </summary>
    [Fact]
    public void The_beam_is_brightest_where_it_moves_slowest()
    {
        // Dim enough that neither place is near white, where the phosphor saturates.
        var drawn = Shown(Drawing((Sine, [(Freq, 100f), (Amp, 0.8f)]), (Value, [(0, 0f)]), (Intensity, 0.02f)).Patch);

        Lit(drawn, 0.8, 0).ShouldBeGreaterThan(Lit(drawn, 0, 0) * 2);
    }

    /// <summary>
    /// A beam that jumped a while ago is still glowing where it was for a long
    /// persistence, and has faded from there for a short one.
    /// </summary>
    [Fact]
    public void The_phosphor_fades_behind_the_beam_as_slowly_as_it_is_told()
    {
        // A quarter of a second at one side, then a twentieth at the other.
        double Older((int, float) persistence)
        {
            var drawn = Shown(
                Drawing((Square, [(Freq, 2f), (Amp, 0.5f)]), (Value, [(0, 0f)]), persistence).Patch,
                seconds: 0.3d);

            var left = Lit(drawn, -0.5, 0);
            var right = Lit(drawn, 0.5, 0);

            return Math.Min(left, right) / Math.Max(left, right);
        }

        Older((Persistence, -2f)).ShouldBeLessThan(0.01);
        Older((Persistence, -0.5f)).ShouldBeGreaterThan(0.2);
    }

    [Fact]
    public void Nothing_played_is_nothing_drawn()
    {
        var (patch, _) = Circle();

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        Lit(drawn, 0.5, 0).ShouldBe(0d);
    }

    /// <summary>
    /// Noise crosses the whole screen every evaluation. Drawing it stays cheap and
    /// still lights the screen, rather than freezing the frame.
    /// </summary>
    [Fact]
    public void Noise_draws_as_a_haze_in_good_time()
    {
        var (patch, _) = Drawing(("audio.noise", []), ("audio.noise", [(2, 1f)]), (Persistence, -1f));

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard);
        renderer.Render(heard, new float[renderer.SampleRate * 2], memory);

        var took = System.Diagnostics.Stopwatch.StartNew();
        Traces.Refresh(drawn, heard, memory);
        took.Stop();

        took.ElapsedMilliseconds.ShouldBeLessThan(250);
        Lit(drawn, 0, 0).ShouldBeGreaterThan(0d);
    }
}
