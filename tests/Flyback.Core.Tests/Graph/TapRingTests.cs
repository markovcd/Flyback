using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Each tap keeps as much of the past as its reader looks at: a Meter's or a
/// Bridge's window, not the 32 seconds a Scope may be turned up to.
/// </summary>
public class TapRingTests
{
    private const string Sine = "osc.sine";

    private const int Window = 1;

    [Fact]
    public void A_meter_keeps_its_window_and_not_the_whole_32_seconds()
    {
        var heard = Watching(MeterModule.TypeId, -1.7f).Heard;
        var memory = Played(heard);

        var ring = memory.Arrays.Traces.ShouldHaveSingleItem().Length;
        var window = (int)Math.Ceiling(MathF.Pow(10f, -1.7f) * memory.SampleRate);

        ring.ShouldBeGreaterThanOrEqualTo(window);
        ring.ShouldBeLessThan(window + 8);
        ring.ShouldBeLessThan(DelayState.TraceSamples / 1000);
    }

    [Fact]
    public void A_bridge_keeps_its_window_too() =>
        Played(Watching(BridgeModule.TypeId, -2f).Heard).Arrays.Traces.ShouldHaveSingleItem().Length
            .ShouldBeLessThan(DelayState.TraceSamples / 1000);

    /// <summary>A transform is at least one shortest segment long, so an Analyzer's ring is too, however short its window.</summary>
    [Fact]
    public void An_analyzer_keeps_one_transform_however_short_its_window() =>
        Played(Watching(ChartModules.AnalyzerTypeId, -4f).Heard).Arrays.Traces.ShouldHaveSingleItem().Length
            .ShouldBeGreaterThan(Spectra.ShortestSegment);

    [Fact]
    public void A_beam_keeps_what_its_trace_may_reach_back_for() =>
        Played(Watching(BeamModule.TypeId, -1f).Heard).Arrays.Traces.ShouldAllBe(ring => ring.Length > Beams.MaxSpan);

    /// <summary>
    /// The window is read at compile time, so turning it rebuilds the program and
    /// the ring with it. The rebuilt ring starts from the old one's newest past,
    /// so the reading does not drop out for a window's length.
    /// </summary>
    [Fact]
    public void Turning_the_window_resizes_the_ring_and_keeps_what_it_heard()
    {
        var renderer = new AudioRenderer();

        var (patch, meter) = Built(MeterModule.TypeId, -1.7f);
        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var memory = renderer.DelayMemoryFor(heard).ShouldNotBeNull();
        renderer.Render(heard, new float[8_192 * 2], memory);

        // Recompiled as it was: the same memory, nothing allocated.
        renderer.DelayMemoryFor(patch.CompileForAudio(NodeCatalog.BuiltIn).Program, memory).ShouldBeSameAs(memory);

        var span = memory.Arrays.Traces[0].Length - 2;
        var before = memory.Measure(0, span);

        meter.InputValues[Window] = -1.3f;
        var resized = renderer.DelayMemoryFor(patch.CompileForAudio(NodeCatalog.BuiltIn).Program, memory).ShouldNotBeNull();

        resized.ShouldNotBeSameAs(memory);
        resized.Arrays.Traces[0].Length.ShouldBeGreaterThan(memory.Arrays.Traces[0].Length);
        resized.Measure(0, span).Level.ShouldBe(before.Level, 1e-5f);
    }

    private static (CompiledPatch Heard, NodeInstance Watcher) Watching(string typeId, float decades)
    {
        var (patch, watcher) = Built(typeId, decades);

        return (patch.CompileForAudio(NodeCatalog.BuiltIn).Program, watcher);
    }

    /// <summary>A sine at the speakers, watched by a <paramref name="typeId"/> whose window knob is <paramref name="decades"/>.</summary>
    private static (Patch Patch, NodeInstance Watcher) Built(string typeId, float decades)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(OutputModule.TypeId, 900, 0, (OutputModule.VolumePort, 1f));
        var source = b.Add(Sine, 0, 0, (1, 220f));
        var watcher = b.Add(typeId, 400, 0, (Window, decades));

        b.Wire(source, 0, watcher, 0).Wire(source, 0, output, OutputModule.LeftPort);
        if (typeId == BeamModule.TypeId) b.Wire(source, 0, watcher, 1);

        return (b.Patch, watcher);
    }

    private static DelayState Played(CompiledPatch heard)
    {
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard).ShouldNotBeNull();

        renderer.Render(heard, new float[1_024 * 2], memory);

        return memory;
    }
}
