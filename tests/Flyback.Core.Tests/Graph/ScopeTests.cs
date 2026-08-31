using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Scope: a chart of what the speakers actually played, as against the
/// Probe's chart of what the screen computes the signal to be.
/// </summary>
/// <remarks>
/// Three things about it are unlike every other module, and everything here is
/// about one of them. Its input is a root of the audio program even though
/// nothing downstream reads it, which is dead-code elimination deliberately
/// given up. Its input is never evaluated by the picture at all, so the chart
/// costs the eye only the table read. And what it draws comes back out of the
/// run that made the sound rather than out of the program drawing it, which is
/// the only path in the instrument that goes that way round.
/// </remarks>
public class ScopeTests
{
    private const string Scope = NodeCatalog.ScopeTypeId;
    private const string Sine = "osc.sine";
    private const string Value = "value";

    private const int In = 0;
    private const int Window = 1;

    /// <summary>
    /// How much of the chart's ink is at a pixel. Green carries the most of it,
    /// the same measurement the Probe's tests take.
    /// </summary>
    private static double Ink(CompiledPatch program, double x, double y, double aspect = 1d)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, registers, default, aspect: aspect);

        return registers[program.OutputBase + 1];
    }

    private static int Count(CompiledPatch program, OpCode code) =>
        program.Ops.Count(op => op.Code == code);

    /// <summary>
    /// A patch whose Output has nothing wired into it at all, and a Scope off to
    /// one side watching <paramref name="watched"/>.
    /// </summary>
    /// <remarks>
    /// Nothing on the sink on purpose. A Scope that only works when the patch
    /// happens to reach it would be no different from an ordinary module, and
    /// the whole claim being tested here is that the walk goes out of its way.
    /// </remarks>
    private static (Patch Patch, NodeInstance Scope, NodeInstance Source) Watching(
        string watched,
        params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 0);

        var source = b.Add(watched, 0, 0);
        var scope = b.Add(Scope, 400, 0, knobs);
        b.Wire(source, 0, scope, In);

        return (b.Patch, scope, source);
    }

    /// <summary>
    /// Runs the sound for long enough to fill whatever window the chart is
    /// asking for, and hands back the memory it filled.
    /// </summary>
    private static DelayState? Played(CompiledPatch heard, int frames = 4096)
    {
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard);

        renderer.Render(heard, new float[frames * 2], AudioScan.TimeDriven, memory);

        return memory;
    }

    [Fact]
    public void A_scope_is_an_ordinary_module_that_a_patch_may_hold_several_of()
    {
        var def = NodeCatalog.BuiltIn.Require(Scope);

        new Patch().CanAdd(Scope).ShouldBeTrue();
        def.Outputs.Count.ShouldBe(1);
        def.TapsSignal.ShouldBeTrue();
    }

    /// <summary>
    /// The default window is a fiftieth of a second, which is a few cycles of
    /// anything audible — a chart of a tone should show a waveform the moment it
    /// is dropped on the canvas rather than a solid block.
    /// </summary>
    [Fact]
    public void The_window_starts_at_a_few_cycles_of_an_audible_tone()
    {
        var window = NodeCatalog.BuiltIn.Require(Scope).Inputs[Window];

        window.Display.ShouldBe(PortDisplay.Duration);
        window.Format(window.Default).ShouldBe("20 ms");
    }

    /// <summary>
    /// The whole of what makes it different: nothing downstream of the Scope
    /// reads anything, so the ordinary walk from the sink would never reach the
    /// oscillator — and does not, until the taps are rooted.
    /// </summary>
    [Fact]
    public void The_sound_evaluates_what_the_scope_watches_even_though_nothing_plays_it()
    {
        var (patch, _, _) = Watching(Sine);

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        Count(heard, OpCode.Sin).ShouldBe(1);
        Count(heard, OpCode.Tap).ShouldBe(1);
        heard.TraceCount.ShouldBe(1);
    }

    /// <summary>
    /// And the eye pays nothing for it. A Scope charts a recording of the past,
    /// so the picture reads a table and never lowers the chain behind it —
    /// which is the opposite of a Probe, whose whole cost is that chain.
    /// </summary>
    [Fact]
    public void The_picture_reads_the_recording_rather_than_the_signal()
    {
        var (patch, scope, _) = Watching(Sine);

        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        Count(drawn, OpCode.Sin).ShouldBe(0);
        Count(drawn, OpCode.Table).ShouldBe(1);
        Count(drawn, OpCode.Tap).ShouldBe(0);
    }

    /// <summary>
    /// The two programs are compiled separately and throw away different dead
    /// code, so what pairs a ring up with the buffer it fills is the node id and
    /// nothing about position.
    /// </summary>
    [Fact]
    public void Both_programs_name_the_same_module()
    {
        var (patch, scope, _) = Watching(Sine);

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        heard.Taps.Select(t => t.Node).ShouldBe([scope.Id]);
        drawn.Taps.Select(t => t.Node).ShouldBe([scope.Id]);

        // The playing side writes a ring and has no buffer; the drawing side has
        // a buffer and writes nothing.
        heard.Taps[0].Trace.Samples.Length.ShouldBe(0);
        drawn.Taps[0].Trace.Samples.Length.ShouldBe(Traces.Points);
    }

    /// <summary>
    /// The round trip, on a signal whose value is known everywhere: run the
    /// sound, refill the chart, and every point of it is what was played.
    /// </summary>
    [Fact]
    public void What_was_played_comes_back_out_in_the_chart()
    {
        var (patch, scope, _) = Watching(Value, (0, 0.5f));

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        var memory = Played(heard);
        Traces.Refresh(drawn, heard, memory);

        drawn.Taps[0].Trace.Samples.ShouldAllBe(v => Math.Abs(v - 0.5f) < 1e-4f);
    }

    /// <summary>
    /// And before the sound has run there is nothing in it. A chart that showed
    /// something before anything was played would be showing the one thing it
    /// promises never to invent.
    /// </summary>
    [Fact]
    public void Nothing_played_is_nothing_charted()
    {
        var (patch, scope, _) = Watching(Value, (0, 0.5f));

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        Traces.Refresh(drawn, heard, new DelayState([], 192_000, traceCount: heard.TraceCount));

        drawn.Taps[0].Trace.Samples.ShouldAllBe(v => v == 0f);
    }

    /// <summary>
    /// The one thing a Probe cannot do. An oscillator's phase is accumulated,
    /// which only happens where evaluations run in order — so a chart of one is
    /// a waveform here and would be a straight multiply on the video path.
    /// </summary>
    [Fact]
    public void A_chart_of_an_oscillator_is_the_wave_that_was_heard()
    {
        // A tone whose period is far shorter than the window, so a fiftieth of a
        // second holds several cycles of it however the buckets fall.
        var (patch, scope, source) = Watching(Sine);
        patch.Find(source.Id).ShouldNotBeNull().InputValues[1] = 220f;

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        Traces.Refresh(drawn, heard, Played(heard));

        var chart = drawn.Taps[0].Trace.Samples;

        chart.Max().ShouldBeGreaterThan(0.8f);
        chart.Min().ShouldBeLessThan(-0.8f);

        // Several crossings rather than one: a chart that had aliased down to a
        // slow wobble would still pass the two lines above.
        var crossings = chart.Zip(chart.Skip(1)).Count(p => p.First < 0f != p.Second < 0f);
        crossings.ShouldBeGreaterThan(4);
    }

    /// <summary>
    /// The far end of the timebase, where a column covers dozens of cycles. What
    /// a scope shows there is the amplitude, as a solid band; what it must not
    /// show is silence, which is exactly what averaging the bucket would give
    /// for a waveform symmetric about nought.
    /// </summary>
    [Fact]
    public void A_sweep_far_slower_than_the_signal_charts_its_amplitude()
    {
        // The far corner of the instrument: the longest window the ring holds
        // against a tone near the top of its range, which is the only
        // combination where a column covers more than one whole cycle.
        var (patch, scope, source) = Watching(Sine, (Window, 0.3f));
        patch.Find(source.Id).ShouldNotBeNull().InputValues[1] = 2000f;

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        Traces.Refresh(drawn, heard, Played(heard, frames: 100_000));

        var chart = drawn.Taps[0].Trace.Samples;

        chart.Max().ShouldBeGreaterThan(0.9f);
        chart.Min().ShouldBeLessThan(-0.9f);

        // And it is a band rather than a line: nearly every column reaches the
        // amplitude, on one side or the other. Averaging the bucket would put
        // every one of them at nought instead.
        chart.Count(v => Math.Abs(v) > 0.9f).ShouldBeGreaterThan(Traces.Points - 64);
    }

    /// <summary>
    /// Which end is now has to be visible, because it is the one thing about the
    /// chart nobody can work out by looking at the trace. A Probe rules a line
    /// down the moment, having a future to draw on the far side of it. This one
    /// has no such column — its moment is the right-hand edge — so it says the
    /// same thing with the phosphor: brightest at the beam, fading back.
    /// </summary>
    [Theory]
    [InlineData(1d)]
    [InlineData(16d / 9d)]
    public void The_trace_is_brightest_at_now_and_fades_into_the_past(double aspect)
    {
        var (patch, scope, _) = Watching(Value, (0, 0.5f));

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        Traces.Refresh(drawn, heard, Played(heard));

        // The same trace at the same height, read at each end of it — so the
        // only thing that can differ is how brightly it is drawn.
        var newest = Ink(drawn, aspect - 0.02d, 0.5d, aspect);
        var oldest = Ink(drawn, -aspect + 0.02d, 0.5d, aspect);

        newest.ShouldBeGreaterThan(oldest * 1.5d);
        oldest.ShouldBeGreaterThan(0.2d);
    }

    /// <summary>
    /// And nothing is ruled down the middle of it, which is what a Probe does
    /// there and would mean half a window ago here. The two share the drawing,
    /// so this is worth pinning on both.
    /// </summary>
    [Fact]
    public void Only_a_probe_rules_the_middle()
    {
        var (patch, scope, _) = Watching(Value, (0, 0f));

        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var probe = b.Add(NodeCatalog.ProbeTypeId, 400, 0);

        // Well clear of the trace and of the horizontal axis, so what is
        // measured is the vertical rule and nothing else. The middle column is
        // a grid line on both charts, which is worth 0.09; a rule on top of it
        // is worth 0.2 more, so the threshold sits between the two.
        Ink(b.Patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn).Program, 0d, 0.6d)
            .ShouldBeGreaterThan(0.15d);

        Ink(patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program, 0d, 0.6d)
            .ShouldBeLessThan(0.15d);
    }

    /// <summary>
    /// The chart reaches both edges of the frame however wide it is. A window
    /// that stopped short would be a chart with the oldest of the past cut off
    /// it and a dead margin either side.
    /// </summary>
    [Theory]
    [InlineData(1d)]
    [InlineData(16d / 9d)]
    [InlineData(2.4d)]
    public void The_window_is_stretched_across_the_whole_frame(double aspect)
    {
        var (patch, scope, _) = Watching(Value, (0, 0.5f));

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        Traces.Refresh(drawn, heard, Played(heard));

        // The trace sits at half of 'scale', so this is where it is and nowhere
        // else — at both edges as well as the middle, which is the whole claim.
        // The upper threshold clears what the phosphor leaves of the oldest
        // column; the lower one is below a bare grid line, and the readings are
        // taken off the graticule so that neither is what is being measured.
        foreach (var column in new[] { -aspect + 0.02d, 0.03d, aspect - 0.02d })
        {
            Ink(drawn, column, 0.5d, aspect).ShouldBeGreaterThan(0.2d);
            Ink(drawn, column, -0.6d, aspect).ShouldBeLessThan(0.05d);
        }
    }

    /// <summary>
    /// A window turned right down asks for fewer evaluations than the chart has
    /// columns, which is the one case where cells have to share.
    /// </summary>
    [Fact]
    public void A_window_shorter_than_the_chart_is_wide_still_draws()
    {
        var (patch, scope, _) = Watching(Value, (0, 0.5f), (Window, -4f));

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var drawn = patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        drawn.Taps[0].Window.ShouldBe(1e-4f, 1e-6f);

        Traces.Refresh(drawn, heard, Played(heard));

        drawn.Taps[0].Trace.Samples.ShouldAllBe(v => Math.Abs(v - 0.5f) < 1e-4f);
    }
}
