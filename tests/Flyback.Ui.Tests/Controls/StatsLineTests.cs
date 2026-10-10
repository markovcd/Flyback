using Avalonia;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Controls;

/// <summary>What the stats line says.</summary>
public class StatsLineTests
{
    [Fact]
    public void The_line_reads_rate_cost_oversampling_size_renderer_sound_backend_and_clock() =>
        StatsOverlay.Line(59.6, 4.26, 2, new PixelSize(960, 540), "Direct3D", "ASIO", 65.25)
            .ShouldBe("60 fps · 4.3 ms · 2× oversampling · 960×540 · Direct3D · ASIO · t 1:05.25");

    [Fact]
    public void Nothing_to_play_through_names_no_sound_backend() =>
        StatsOverlay.Line(59.6, 4.26, 2, new PixelSize(960, 540), "Direct3D", null, 65.25)
            .ShouldBe("60 fps · 4.3 ms · 2× oversampling · 960×540 · Direct3D · t 1:05.25");

    [Fact]
    public void A_context_still_coming_up_names_no_renderer() =>
        StatsOverlay.Line(59.6, 4.26, 2, new PixelSize(960, 540), null, null, 65.25)
            .ShouldBe("60 fps · 4.3 ms · 2× oversampling · 960×540 · t 1:05.25");

    [Fact]
    public void A_patch_with_no_sound_leaves_the_oversampling_out() =>
        StatsOverlay.Line(59.6, 4.26, null, new PixelSize(960, 540), "Direct3D", null, 65.25)
            .ShouldBe("60 fps · 4.3 ms · 960×540 · Direct3D · t 1:05.25");

    [Fact]
    public void One_times_is_no_oversampling() =>
        StatsOverlay.Line(59.6, 4.26, 1, new PixelSize(960, 540), "Direct3D", null, 65.25)
            .ShouldBe("60 fps · 4.3 ms · no oversampling · 960×540 · Direct3D · t 1:05.25");
}
