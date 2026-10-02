using Flyback.Core.Graph;
using Flyback.Engine.Measure;
using Shouldly;

namespace Flyback.Core.Tests.Measure;

/// <summary>
/// A measurement runs the patch offline and says what every output carried, wired
/// to anything or not, to the speakers and to the screen.
/// </summary>
public class MeasurementsTests
{
    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    private static MeasureReport Take(PatchBuilder b, double seconds = 1d, IReadOnlyCollection<Guid>? only = null, bool interpreted = false) =>
        Measurements.Take(b.Patch, new MeasureOptions(seconds, Modules: only, Interpreted: interpreted), Modules);

    private static Measurement Of(MeasureReport report, NodeInstance node, int port = 0) =>
        report.Measurements.Single(m => m.Node == node.Id && m.Port == port);

    [Fact]
    public void An_oscillator_wired_to_nothing_is_measured_at_its_pitch()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        var tone = b.Add("osc.sine", (1, 440f));

        var sound = Of(Take(b), tone).Sound.Single();

        sound.OverTime.ShouldBeTrue();
        sound.Hz!.Value.ShouldBe(440d, 0.5d);
        sound.Min.ShouldBe(-1d, 1e-3);
        sound.Max.ShouldBe(1d, 1e-3);
    }

    [Fact]
    public void An_output_that_holds_still_is_given_as_its_value_in_both_halves()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        var still = b.Add("osc.sine", (3, 0f), (4, 0.25f));

        var measured = Of(Take(b), still);

        measured.Sound.Single().Static.ShouldBeTrue();
        measured.Sound.Single().Min.ShouldBe(0.25d, 1e-6);
        measured.Picture.Single().Static.ShouldBeTrue();
        measured.Differs.ShouldBeFalse();
    }

    [Fact]
    public void A_coordinate_is_still_to_the_ear_and_spread_across_the_picture()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        var coords = b.Add("coord");

        var x = Of(Take(b), coords);

        x.Sound.Single().Static.ShouldBeTrue();
        x.Picture.Single().Across.ShouldBeTrue();
        x.Picture.Single().OverTime.ShouldBeFalse();
        x.Differs.ShouldBeTrue();
    }

    [Fact]
    public void A_sequencer_is_counted_in_steps_a_second()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        var steps = b.Add("seq.values", (1, 4f));

        var measured = Of(Take(b, seconds: 2d), steps);

        measured.Sound.Single().StepsPerSecond!.Value.ShouldBe(4d, 0.1d);
        measured.Picture.Single().StepsPerSecond!.Value.ShouldBe(4d, 0.1d);
    }

    [Fact]
    public void A_color_is_measured_as_its_red_green_and_blue()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        var color = b.Add("color.hsv");

        var measured = Of(Take(b), color);

        measured.Sound.Count.ShouldBe(3);
        measured.Picture.Count.ShouldBe(3);
    }

    [Fact]
    public void Measuring_one_module_reports_only_its_outputs()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        var tone = b.Add("osc.sine");
        b.Add("coord");

        var report = Take(b, only: [tone.Id]);

        report.Measurements.ShouldAllBe(m => m.Node == tone.Id);
        report.Measurements.ShouldNotBeEmpty();
    }

    /// <summary>The IL is only a faster way to the interpreter's numbers (ADR-0035).</summary>
    [Fact]
    public void The_interpreter_and_the_IL_measure_the_same()
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);
        b.Add("osc.sine", (1, 3f));
        b.Add("seq.values");
        b.Add("audio.noise");

        var il = Take(b, interpreted: false);
        var interpreted = Take(b, interpreted: true);

        il.Measurements.ShouldBe(interpreted.Measurements, new MeasurementComparer());
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(MeasureOptions.MaxSeconds + 1d)]
    public void A_window_that_is_not_a_positive_length_is_refused(double seconds)
    {
        var b = new PatchBuilder(Modules);
        b.Add(NodeCatalog.OutputTypeId);

        Should.Throw<ArgumentOutOfRangeException>(() => Take(b, seconds));
    }

    private sealed class MeasurementComparer : IEqualityComparer<Measurement>
    {
        public bool Equals(Measurement? a, Measurement? b) =>
            a is not null && b is not null
            && a.Node == b.Node && a.Port == b.Port
            && a.Sound.SequenceEqual(b.Sound) && a.Picture.SequenceEqual(b.Picture);

        public int GetHashCode(Measurement m) => HashCode.Combine(m.Node, m.Port);
    }
}
