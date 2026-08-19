using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Probe: one socket, and a picture of the value arriving at it rather than
/// a picture made from it.
/// </summary>
/// <remarks>
/// Two things about it are unlike every other module, and everything here is
/// about one or the other. It is a root — a program compiled for a probe never
/// reaches the Output, so the patch's own picture costs nothing while the chart
/// is up. And its input is read over a domain of its own, so the same module
/// resolved inside the sweep and outside it is two values rather than one shared
/// register.
/// </remarks>
public class ProbeTests
{
    private const string Probe = NodeCatalog.ProbeTypeId;
    private const string Sine = "osc.sine";
    private const string Time = "time";
    private const string Coordinates = "coord";

    // Sockets on the probe, named because a shifted one would otherwise be a
    // silent change of meaning here as much as in the catalogue.
    private const int In = 0;
    private const int Window = 1;
    private const int Scale = 2;

    /// <summary>What one pixel of a program comes to, in linear RGB.</summary>
    private static (double R, double G, double B) Pixel(CompiledPatch program, double x, double y, double t)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, t, registers, default);

        return (
            registers[program.OutputBase + 0],
            registers[program.OutputBase + 1],
            registers[program.OutputBase + 2]);
    }

    /// <summary>How much of the chart's ink is at a pixel. Green is the trace color, so it carries the most.</summary>
    private static double Ink(CompiledPatch program, double x, double y, double t) =>
        Pixel(program, x, y, t).G;

    private static int Count(CompiledPatch program, OpCode code) =>
        program.Ops.Count(op => op.Code == code);

    /// <summary>
    /// A patch with a picture on the Output and a probe beside it, wired to
    /// whatever <paramref name="watched"/> names. The two halves share nothing,
    /// so what each program costs can be read off the ops.
    /// </summary>
    private static (Patch Patch, NodeInstance Probe) Watching(
        string watched,
        int port = 0,
        params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var seen = b.Add(Coordinates, 0, 0);
        var picture = b.Add(Sine, 300, 0);
        b.Wire(seen, 0, picture, 0).Wire(picture, 0, output, NodeCatalog.OutputColorPort);

        var source = b.Add(watched, 300, 300);
        var probe = b.Add(Probe, 600, 300, knobs);
        b.Wire(source, port, probe, In);

        return (b.Patch, probe);
    }

    [Fact]
    public void A_probe_is_an_ordinary_module_that_a_patch_may_hold_several_of()
    {
        var patch = new Patch();

        patch.CanAdd(Probe).ShouldBeTrue();
        NodeCatalog.BuiltIn.Get(Probe).ShouldNotBeNull().Outputs.Count.ShouldBe(1);
    }

    /// <summary>
    /// The timebase is marked in decades, because no linear knob is any use
    /// across the range this one needs: a chart of an audible tone wants a
    /// millisecond and a chart of an LFO wants half a minute, and on a slider
    /// running to thirty seconds every audio-rate setting is inside the first
    /// thousandth of the travel.
    /// </summary>
    [Fact]
    public void The_timebase_reaches_from_one_audio_cycle_to_a_slow_LFO()
    {
        var window = NodeCatalog.BuiltIn.Require(Probe).Inputs[Window];

        window.Display.ShouldBe(PortDisplay.Duration);
        window.Format(window.Min).ShouldBe("100 µs");
        window.Format(-3f).ShouldBe("1 ms");
        window.Format(window.Default).ShouldBe("2 s");
        window.Format(window.Max).ShouldBe("31.62 s");
    }

    /// <summary>
    /// The point of rooting at the probe rather than drawing over the picture:
    /// what the Output would have shown is not merely covered up, it is never
    /// compiled. Here that is the whole of the patch's own picture, which is the
    /// only thing in either program with a Sin in it.
    /// </summary>
    [Fact]
    public void Charting_costs_the_picture_nothing_because_the_Output_is_never_walked()
    {
        var (patch, probe) = Watching(Time);

        Count(patch.CompileForVideo(NodeCatalog.BuiltIn).Program, OpCode.Sin).ShouldBe(1);
        Count(patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program, OpCode.Sin).ShouldBe(0);
    }

    /// <summary>
    /// Time runs across the picture. Charting Time itself is the cleanest way to
    /// say so: the trace is a straight line whose height at each column is the
    /// moment that column stands for, so where the ink is says exactly which
    /// substitution the sweep made.
    /// </summary>
    /// <param name="x">Which column of the chart to read.</param>
    /// <param name="decades">
    /// The timebase, in powers of ten of seconds: a two-second window and a
    /// millisecond one, which is the span the knob has to cover for a probe to
    /// be any use at an audible pitch as well as at an LFO's.
    /// </param>
    [Theory]
    [InlineData(0d, 0.3f)]
    [InlineData(0.3d, 0.3f)]
    [InlineData(-0.7d, 0.3f)]
    [InlineData(0.3d, -3f)]
    [InlineData(-0.7d, -3f)]
    public void The_column_is_the_moment_and_the_middle_column_is_now(double x, float decades)
    {
        const float scale = 4f;
        const double now = 1d;

        var (patch, probe) = Watching(Time, knobs: [(Window, decades), (Scale, scale)]);
        var program = patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program;

        // What the module under the probe is worth at this column, and where
        // that lands on a chart whose top edge is 'scale'.
        var when = now + x * Math.Pow(10d, decades) * 0.5d;
        var height = when / scale;

        Ink(program, x, height, now).ShouldBeGreaterThan(0.9d, "on the trace");
        Ink(program, x, height + 0.4d, now).ShouldBeLessThan(0.35d, "well above it");
    }

    /// <summary>
    /// x and y are pinned while the sweep is in force, so what is charted is the
    /// signal at the middle of the picture. A module that draws with Coordinates
    /// has a value per pixel and there is no one line that is all of them —
    /// which is the honest thing for the chart to say, rather than picking the
    /// column it happens to be drawing at.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_swept_signal_is_read_at_the_middle_of_the_picture(int axis)
    {
        var (patch, probe) = Watching(Coordinates, axis, (Scale, 1f));
        var program = patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program;

        // Flat at zero however far along the chart it is read, because the
        // coordinate it is charting is zero everywhere the sweep looks.
        Ink(program, 0.6d, 0d, 0.5d).ShouldBeGreaterThan(0.9d);
        Ink(program, 0.6d, 0.5d, 0.5d).ShouldBeLessThan(0.35d);
    }

    /// <summary>
    /// The chart itself is still drawn at the pixel's own coordinates. It has to
    /// be — the sweep is what the trace is <em>of</em>, and a probe that read its
    /// own y through the substitution would have nowhere to put the line.
    /// </summary>
    [Fact]
    public void The_domain_goes_back_when_the_sweep_is_done()
    {
        var (patch, probe) = Watching(Time, knobs: [(Scale, 1f)]);
        var program = patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program;

        // One LoadX and one LoadY, both the drawing's own: the sweep hands the
        // subtree registers rather than loads, so nothing it resolved added any.
        Count(program, OpCode.LoadX).ShouldBe(1);
        Count(program, OpCode.LoadY).ShouldBe(1);
        Count(program, OpCode.LoadT).ShouldBe(1);
    }

    /// <summary>
    /// A module read at one moment and the same module read at another are two
    /// values, whatever they share in the graph. Sharing the register would
    /// chart the wrong moment, so the sweep resolves under its own cache.
    /// </summary>
    [Fact]
    public void A_module_read_inside_the_sweep_and_outside_it_is_lowered_twice()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Patch.EnsureOutput(NodeCatalog.BuiltIn);

        var clock = b.Add(Time, 0, 0);
        var shared = b.Add(Sine, 300, 0);
        var probe = b.Add(Probe, 600, 0);

        b.Wire(clock, 0, shared, 0).Wire(shared, 0, probe, In);

        Count(b.Patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program, OpCode.Sin).ShouldBe(1);

        // The same oscillator now also sets how much time the chart shows, which
        // is read at the frame's own moment rather than at the swept one.
        b.Wire(shared, 0, probe, Window);

        Count(b.Patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program, OpCode.Sin).ShouldBe(2);
    }

    /// <summary>
    /// A probe is a module besides being a root, so its chart can be patched on
    /// like any other color — which is what keeps it on screen next to the
    /// thing it is charting rather than instead of it.
    /// </summary>
    [Fact]
    public void The_chart_is_also_a_color_the_Output_can_take()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var clock = b.Add(Time, 0, 0);
        var probe = b.Add(Probe, 300, 0, (Scale, 1f));

        b.Wire(clock, 0, probe, In).Wire(probe, 0, output, NodeCatalog.OutputColorPort);

        var result = b.Patch.CompileForVideo(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse();

        // The trace of a clock running at one, half a second in, is at half the
        // height of a chart whose top edge is one.
        Ink(result.Program, 0d, 0.5d, 0.5d).ShouldBeGreaterThan(0.9d);
    }

    [Fact]
    public void An_empty_probe_says_so_rather_than_charting_its_knob_in_silence()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Patch.EnsureOutput(NodeCatalog.BuiltIn);
        var probe = b.Add(Probe, 300, 0);

        var result = b.Patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn);

        result.HasErrors.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.NodeId == probe.Id && i.Message.Contains("Probe"));
    }

    /// <summary>
    /// A selection outliving the module it named is the ordinary way to get
    /// here, and a black screen with nothing to say why is the wrong answer to
    /// it.
    /// </summary>
    [Fact]
    public void A_probe_that_is_no_longer_there_compiles_the_picture()
    {
        var (patch, probe) = Watching(Time);
        patch.Remove(probe.Id);

        var result = patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn);

        Count(result.Program, OpCode.Sin).ShouldBe(1);
        result.Program.Ops.Length.ShouldBe(patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops.Length);
    }

    /// <summary>
    /// Nothing about a chart is special to the interpreter, which is the whole
    /// reason to draw one out of ops rather than out of read-back samples: the
    /// GPU path takes it without being told it exists.
    /// </summary>
    [Theory]
    [InlineData(GlslDialect.GlslEs300)]
    [InlineData(GlslDialect.Glsl150)]
    public void The_chart_lowers_to_a_shader_like_anything_else(GlslDialect dialect)
    {
        var (patch, probe) = Watching(Time);
        var program = patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program;

        GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrWhiteSpace();
    }
}
