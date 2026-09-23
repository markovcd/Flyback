using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.Figures;

namespace Flyback.Plugins.Tests;

/// <summary>The harmonograph's drawing and its chord are the same pendulums.</summary>
public class HarmonographTests
{
    private const int Trigger = 0, Damping = 6, Persist = 7;
    private const int Left = 0, Right = 1, Figure = 2;

    [Fact]
    public void Figures_offers_it_for_both_sinks_with_its_own_face()
    {
        var def = Catalog.Get(Harmonograph).ShouldNotBeNull();

        def.Name.ShouldBe("Harmonograph");
        def.Category.ShouldBe("Figures");
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Skin.ShouldBeOfType<ModuleSkin.Artwork>();
        def.Outputs.Select(o => o.Name).ShouldBe(["left", "right", "figure"]);
    }

    [Fact]
    public void It_is_silent_until_set_swinging()
    {
        Heard(Wired(Harmonograph, Left, NodeCatalog.OutputLeftPort), new float[Rate / 10]).ShouldAllBe(s => s == 0f);
    }

    [Fact]
    public void The_chord_swings_and_dies_by_its_damping()
    {
        var heard = Heard(Wired(Harmonograph, Right, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (Damping, 0.5f)), Pulse(1, Rate * 2));

        Rms(heard.AsSpan(0, Rate / 20)).ShouldBeGreaterThan(0.2);
        Rms(heard.AsSpan(Rate * 2 - Rate / 20)).ShouldBeLessThan(0.02);
        heard.ShouldAllBe(s => float.IsFinite(s) && Math.Abs(s) <= 1f);
    }

    [Fact]
    public void The_two_axes_are_two_different_signals()
    {
        var left = Heard(Wired(Harmonograph, Left, NodeCatalog.OutputLeftPort), Pulse(1, Rate / 10));
        var right = Heard(Wired(Harmonograph, Right, NodeCatalog.OutputLeftPort), Pulse(1, Rate / 10));

        left.Zip(right).Count(pair => Math.Abs(pair.First - pair.Second) > 0.01f).ShouldBeGreaterThan(Rate / 40);
    }

    /// <summary>The screen's program with the strike on Coordinates' aspect, so a pixel can be struck without moving.</summary>
    private static Pixel Drawn(params (int Port, float Value)[] knobs) =>
        new(Wired(Harmonograph, Figure, NodeCatalog.OutputColorPort, Trigger, NodeCatalog.CoordAspectPort, knobs));

    /// <summary>At the strike the pen stands at the top of its swing, and with no persistence only the fresh stroke is inked.</summary>
    [Fact]
    public void The_pen_inks_where_it_is_and_nowhere_else()
    {
        var atPen = Drawn((Persist, 0f));
        var elsewhere = Drawn((Persist, 0f));

        atPen.Planes.ShouldBe(5);

        // 'size' 0.8 puts the pen at (0, 0.8) as it is struck.
        atPen.Frame(0d, 0.8, aspect: 1d).ShouldBeGreaterThan(0.5);
        elsewhere.Frame(0.5, -0.5, aspect: 1d).ShouldBe(0d);
    }

    [Fact]
    public void Ink_fades_by_persist_a_second_once_the_pen_has_moved_on()
    {
        var kept = Drawn((Persist, 0.5f));
        var gone = Drawn((Persist, 0f));

        kept.Frame(0d, 0.8, aspect: 1d).ShouldBeGreaterThan(0.5);
        gone.Frame(0d, 0.8, aspect: 1d).ShouldBeGreaterThan(0.5);

        // A second on, the pen is at the bottom of its swing.
        for (var frame = 0; frame < 59; frame++)
        {
            kept.Frame(0d, 0.8, aspect: 0d);
            gone.Frame(0d, 0.8, aspect: 0d);
        }

        kept.Frame(0d, 0.8, aspect: 0d).ShouldBeInRange(0.3, 0.7);
        gone.Frame(0d, 0.8, aspect: 0d).ShouldBe(0d);
    }

    [Fact]
    public void Nothing_is_inked_before_a_strike()
    {
        var pixel = Drawn();

        for (var frame = 0; frame < 30; frame++)
            pixel.Frame(0d, 0.8, aspect: 0d).ShouldBe(0d);
    }

    [Fact]
    public void The_drawing_survives_to_the_shader()
    {
        var program = Wired(Harmonograph, Figure, NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;

        program.PlaneCount.ShouldBe(5);
        LowersToEveryDialect(program);
    }

    /// <summary>
    /// A plane write is a root the compiler keeps, so the speakers' program carries the
    /// ink and the last frame too, as cells nobody reads: the price of one lowering for
    /// both sinks, about thirty ops a sample.
    /// </summary>
    [Fact]
    public void The_chord_keeps_its_strike_in_planes_and_no_cell()
    {
        var program = Wired(Harmonograph, Left, NodeCatalog.OutputLeftPort).CompileForAudio(Catalog).Program;

        program.PlaneCount.ShouldBe(5);
        program.UnitCount.ShouldBe(0);
    }
}
