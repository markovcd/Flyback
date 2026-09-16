using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Analyzer: a Scope whose buffer holds the spectrum of what was played
/// rather than the waveform, drawn on a log-frequency axis in decibels.
/// </summary>
public class AnalyzerTests
{
    private const string Analyzer = NodeCatalog.AnalyzerTypeId;
    private const string Sine = "osc.sine";

    private const int In = 0;
    private const int Window = 1;
    private const int Scale = 3;

    private const int Frequency = 1;
    private const int Amplitude = 3;

    private static double Ink(CompiledPatch program, double x, double y, double aspect = 1d)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, registers, default, aspect: aspect);

        return registers[program.OutputBase + 1];
    }

    private static int Count(CompiledPatch program, OpCode code) =>
        program.Ops.Count(op => op.Code == code);

    /// <summary>
    /// A tone off to one side of a patch whose Output has nothing wired into it,
    /// and an Analyzer listening to it — the same arrangement the Scope's tests
    /// use, for the same reason.
    /// </summary>
    private static (Patch Patch, NodeInstance Analyzer) Listening(
        float hertz,
        float amplitude = 1f,
        params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 0);

        var tone = b.Add(Sine, 0, 0, (Frequency, hertz), (Amplitude, amplitude));
        var analyzer = b.Add(Analyzer, 400, 0, knobs);
        b.Wire(tone, 0, analyzer, In);

        return (b.Patch, analyzer);
    }

    private static (CompiledPatch Heard, CompiledPatch Drawn) Compiled(Patch patch, NodeInstance analyzer) =>
        (patch.CompileForAudio(NodeCatalog.BuiltIn).Program,
         patch.CompileForProbe(analyzer.Id, NodeCatalog.BuiltIn).Program);

    /// <summary>Plays a third of a second — longer than the default window.</summary>
    private static DelayState? Played(CompiledPatch heard, int frames = 16_384)
    {
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard);

        renderer.Render(heard, new float[frames * 2], memory);

        return memory;
    }

    private static float At(float[] chart, double hertz) =>
        chart[(int)Math.Round(Spectra.PointOf(hertz, chart.Length))];

    /// <summary>Where across a frame of <paramref name="aspect"/> a frequency is drawn.</summary>
    private static double Column(double hertz, double aspect = 1d) =>
        -aspect + 2d * aspect * Spectra.PointOf(hertz, Traces.Points) / (Traces.Points - 1);

    [Fact]
    public void An_analyzer_is_a_chart_that_taps_the_speakers()
    {
        var def = NodeCatalog.BuiltIn.Require(Analyzer);

        new Patch().CanAdd(Analyzer).ShouldBeTrue();
        NodeCatalog.IsChart(Analyzer).ShouldBeTrue();

        def.TapsSignal.ShouldBeTrue();
        def.ChartsSignal.ShouldBeTrue();
        def.ChartsSpectrum.ShouldBeTrue();
        def.Inputs[Window].Display.ShouldBe(PortDisplay.Duration);
    }

    /// <summary>
    /// As with a Scope, the sound evaluates what is listened to though nothing
    /// plays it, and the picture reads a table and never the tone itself.
    /// </summary>
    [Fact]
    public void The_sound_is_tapped_and_the_picture_reads_the_recording()
    {
        var (patch, analyzer) = Listening(440f);
        var (heard, drawn) = Compiled(patch, analyzer);

        Count(heard, OpCode.Sin).ShouldBe(1);
        Count(heard, OpCode.Tap).ShouldBe(1);

        Count(drawn, OpCode.Sin).ShouldBe(0);
        Count(drawn, OpCode.Table).ShouldBe(1);

        drawn.Taps.ShouldHaveSingleItem().Spectrum.ShouldBeTrue();
    }

    /// <summary>And a Scope's buffer goes on holding the waveform.</summary>
    [Fact]
    public void Only_the_analyzer_asks_for_a_spectrum()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var scope = b.Add(NodeCatalog.ScopeTypeId, 400, 0);

        b.Patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program
            .Taps.ShouldHaveSingleItem().Spectrum.ShouldBeFalse();
    }

    /// <summary>
    /// The calibration and the axis together: a full-scale sine reads one at its
    /// own frequency, and next to nothing an octave either side.
    /// </summary>
    [Theory]
    [InlineData(110f)]
    [InlineData(1000f)]
    [InlineData(7040f)]
    public void A_tone_stands_at_its_frequency_at_its_amplitude(float hertz)
    {
        var (patch, analyzer) = Listening(hertz);
        var (heard, drawn) = Compiled(patch, analyzer);

        Traces.Refresh(drawn, heard, Played(heard));

        var chart = drawn.Taps[0].Trace.Samples;

        At(chart, hertz).ShouldBeInRange(0.85f, 1.05f);
        At(chart, hertz / 2d).ShouldBeLessThan(0.01f);
        At(chart, hertz * 2d).ShouldBeLessThan(0.01f);
    }

    /// <summary>Amplitude is linear in the buffer, so half as loud reads half.</summary>
    [Fact]
    public void Half_as_loud_reads_half()
    {
        var (patch, analyzer) = Listening(1000f, 0.5f);
        var (heard, drawn) = Compiled(patch, analyzer);

        Traces.Refresh(drawn, heard, Played(heard));

        At(drawn.Taps[0].Trace.Samples, 1000d).ShouldBeInRange(0.42f, 0.53f);
    }

    /// <summary>
    /// A window longer than a segment is averaged rather than transformed whole,
    /// and the far end of the knob must still read the tone rather than silence.
    /// </summary>
    [Fact]
    public void A_long_window_still_reads_the_tone()
    {
        var (patch, analyzer) = Listening(1000f, knobs: (Window, 0f));
        var (heard, drawn) = Compiled(patch, analyzer);

        drawn.Taps[0].Window.ShouldBe(1f, 1e-4f);

        Traces.Refresh(drawn, heard, Played(heard, GlobalConstants.SampleRate + 1_000));

        At(drawn.Taps[0].Trace.Samples, 1000d).ShouldBeInRange(0.85f, 1.05f);
    }

    [Fact]
    public void Nothing_played_is_nothing_charted()
    {
        var (patch, analyzer) = Listening(1000f);
        var (heard, drawn) = Compiled(patch, analyzer);

        Traces.Refresh(drawn, heard, new DelayState([], 192_000, traceCount: heard.TraceCount));

        drawn.Taps[0].Trace.Samples.ShouldAllBe(v => v == 0f);
    }

    /// <summary>
    /// What reaches the screen: the reading divided by 'scale' and put into
    /// decibels against the default 96 dB range — and the chart stands up from the
    /// bottom, filled beneath the trace, with nothing drawn above a frequency that
    /// was not played.
    /// </summary>
    /// <remarks>
    /// The height is worked out from what the buffer holds rather than from the
    /// tone's own amplitude, because a Hann window reads a tone that falls between
    /// two bins up to about a decibel and a half low, and a trace is thinner than
    /// that. <see cref="A_tone_stands_at_its_frequency_at_its_amplitude"/> pins the
    /// reading itself.
    /// </remarks>
    [Theory]
    [InlineData(1d)]
    [InlineData(16d / 9d)]
    public void The_chart_stands_the_tone_up_from_the_bottom_in_decibels(double aspect)
    {
        var (patch, analyzer) = Listening(1000f, knobs: (Scale, 2f));
        var (heard, drawn) = Compiled(patch, analyzer);

        Traces.Refresh(drawn, heard, Played(heard));

        var tone = Column(1000d, aspect);
        var height = 1d + 2d * (20d * Math.Log10(At(drawn.Taps[0].Trace.Samples, 1000d) / 2d)) / 96d;

        Ink(drawn, tone, height, aspect).ShouldBeGreaterThan(0.5d);
        Ink(drawn, tone, -0.3d, aspect).ShouldBeGreaterThan(0.15d);

        // Off the grid lines in both directions, so a bare square reads as nothing.
        Ink(drawn, Column(300d, aspect), 0.6d, aspect).ShouldBeLessThan(0.05d);
    }
}
