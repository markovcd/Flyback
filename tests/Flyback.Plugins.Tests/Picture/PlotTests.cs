using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Picture;

/// <summary>
/// The Plot module, built from text and read point by point as the renderer reads it.
/// </summary>
/// <remarks>
/// Every chart here is the same box: two screen units square, pitch 0 at the
/// bottom and 4 at the top, four beats across at 60 bpm, so a beat is a second,
/// half a screen unit across, and a pitch is half a unit up.
/// </remarks>
public class PlotTests
{
    private const string PlotType = "flyback.picture.plot";

    private const string Box =
        "bpm: 60, span: 4, low: 0, high: 4, left: -1, right: 1, bottom: -1, top: 1, glow: 0";

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_picture_plugin_offers_it_beside_the_forms()
    {
        var def = Catalog.Get(PlotType).ShouldNotBeNull();

        def.Name.ShouldBe("Plot");
        def.Category.ShouldBe(ModuleCategories.Forms);
        def.Outputs.Select(p => p.Name).ShouldBe(["out", "ink"]);
        Catalog.ProviderOf(PlotType)!.Id.ShouldBe("flyback.picture");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "plot").ShouldBe(1);
    }

    [Fact]
    public void A_steady_pitch_is_a_line_at_its_height()
    {
        var plot = Built("2", "page: 0, future: 1, dot: 0");

        plot.At(0.5f, 0f, 3d).ShouldBe(1f, 1e-5f);
        plot.At(-0.5f, 0f, 3d).ShouldBe(1f, 1e-5f);
        plot.At(0.5f, 0.1f, 3d).ShouldBe(0f);
        plot.At(0.5f, -0.1f, 3d).ShouldBe(0f);
    }

    [Fact]
    public void A_curve_is_drawn_white_without_a_color_wired()
    {
        var plot = Built("2", "page: 0, future: 1, dot: 0");

        plot.Color(0.5f, 0f, 3d).ShouldBe((1f, 1f, 1f));
    }

    [Fact]
    public void Each_column_is_the_function_at_that_columns_beat()
    {
        // At two seconds a page of four beats starts at beat 0: x = -0.75 is beat 0.5
        // and x = 0.25 is beat 2.5, each a quarter of a beat from the step either side.
        var plot = Built("floor(t)", "page: 1, future: 1, dot: 0");

        plot.At(-0.75f, -1f, 2d).ShouldBe(1f, 1e-5f);
        plot.At(-0.75f, -0.5f, 2d).ShouldBe(0f);
        plot.At(0.25f, 0f, 2d).ShouldBe(1f, 1e-5f);
        plot.At(0.25f, -0.5f, 2d).ShouldBe(0f);
    }

    [Fact]
    public void A_jump_is_drawn_as_a_stroke_from_one_value_to_the_next()
    {
        var plot = Built("floor(t)", "page: 1, future: 1, dot: 0");

        // Just past beat 2, between pitch 1 (-0.5) and pitch 2 (0).
        plot.At(0.004f, -0.25f, 2d).ShouldBe(1f, 1e-5f);
        plot.At(0.1f, -0.25f, 2d).ShouldBe(0f);
    }

    [Fact]
    public void A_page_draws_what_is_still_to_come_at_future_brightness()
    {
        var hidden = Built("2", "page: 1, future: 0, dot: 0");
        var dim = Built("2", "page: 1, future: 0.25, dot: 0");

        // At one second now is x = -0.5: the left is played, the right is to come.
        hidden.At(-0.75f, 0f, 1d).ShouldBe(1f, 1e-5f);
        hidden.At(0.5f, 0f, 1d).ShouldBe(0f);
        dim.At(0.5f, 0f, 1d).ShouldBe(0.25f, 1e-5f);
    }

    [Fact]
    public void A_page_turns_every_span_of_beats()
    {
        var plot = Built("floor(t / 4)", "page: 1, future: 1, dot: 0");

        // Beats 4 to 8 are pitch 1, all across the second page.
        plot.At(-0.9f, -0.5f, 5d).ShouldBe(1f, 1e-5f);
        plot.At(0.9f, -0.5f, 5d).ShouldBe(1f, 1e-5f);
    }

    [Fact]
    public void Scrolling_holds_now_where_ahead_says()
    {
        // A quarter of the span still to come puts now at x = 0.5.
        var plot = Built("t - 8", "page: 0, ahead: 0.25, future: 1, dot: 0");

        // At ten seconds, x = 0.5 is beat 10 (pitch 2, y = 0) and x = 0 is beat 9.
        plot.At(0.5f, 0f, 10d).ShouldBe(1f, 1e-5f);
        plot.At(0f, -0.5f, 10d).ShouldBe(1f, 1e-5f);
        plot.At(0f, 0f, 10d).ShouldBe(0f);
    }

    [Fact]
    public void The_dot_rides_the_curve_at_now()
    {
        var plot = Built("2", "page: 1, future: 0, dot: 0.05, width: 0.001");

        // At one second now is x = -0.5; a page leaves the future dark, so only the dot is there.
        plot.At(-0.5f + 0.01f, 0.01f, 1d).ShouldBe(1f, 1e-5f);
        plot.At(0.5f, 0.02f, 1d).ShouldBe(0f);
    }

    [Fact]
    public void Nothing_is_drawn_outside_the_box()
    {
        var plot = Built("2", "page: 0, future: 1, dot: 0");

        plot.At(1.1f, 0f, 3d).ShouldBe(0f);
        plot.At(-1.1f, 0f, 3d).ShouldBe(0f);
    }

    [Fact]
    public void Level_dims_the_curve_and_under_shows_through()
    {
        var plot = Built("2", "page: 0, future: 1, dot: 0, level: 0.5, under: rgb(0.1, 0.2, 0.3)");

        plot.Color(0.5f, 0f, 3d).ShouldBe((0.6f, 0.7f, 0.8f));
        plot.Color(0.5f, 0.5f, 3d).ShouldBe((0.1f, 0.2f, 0.3f));
    }

    [Fact]
    public void The_grid_rules_whole_pitches_and_now()
    {
        var plot = Built("2", "page: 0, future: 1, dot: 0, grid: 1");

        // Pitch 1 is y = -0.5, and now is x = 0.
        plot.At(0.3f, -0.5f, 3d).ShouldBe(0.3f, 1e-5f);
        plot.At(0f, 0.3f, 3d).ShouldBe(0.3f, 1e-5f);
        plot.At(0.3f, -0.3f, 3d).ShouldBe(0f);
    }

    [Fact]
    public void It_survives_to_the_shader()
    {
        var program = Built("floor(t) + sin(t)", "page: 1, dot: 0.03, grid: 0.5").Program;

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void It_costs_the_sound_nothing()
    {
        var built = PatchLanguage.Build($"plot(in: floor(t), {Box}) |> out.color", Catalog);
        built.Issues.ShouldBeEmpty();

        Catalog.Require(PlotType).Sinks.ShouldBe(ModuleSinks.Video);
    }

    // --- harness -----------------------------------------------------------------

    private static Chart Built(string pitch, string knobs)
    {
        var built = PatchLanguage.Build($"plot(in: {pitch}, {Box}, {knobs}) |> out.color", Catalog);
        built.Issues.ShouldBeEmpty();

        return new Chart(built.Patch.CompileForVideo(Catalog).Program);
    }

    private sealed record Chart(CompiledPatch Program)
    {
        public float At(float x, float y, double t) => Color(x, y, t).R;

        public (float R, float G, float B) Color(float x, float y, double t)
        {
            var bank = Program.AllocateRegisters();
            Program.Evaluate(x, y, t, bank, default);

            return (Round(bank[Program.OutputBase]), Round(bank[Program.OutputBase + 1]), Round(bank[Program.OutputBase + 2]));
        }

        private static float Round(double value) => MathF.Round((float)value, 4);
    }
}
