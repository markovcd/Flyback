using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>Which monitor the full-screen picture goes to.</summary>
public class MonitorPickTests
{
    private static MonitorSpot Monitor(string name, int x, int width = 1920, int height = 1080) =>
        new() { Name = name, X = x, Y = 0, Width = width, Height = height };

    // Listed out of desktop order, as a platform may.
    private static readonly MonitorSpot[] Three =
    [
        Monitor("middle", 0),
        Monitor("right", 1920, 2560, 1440),
        Monitor("left", -1920),
    ];

    [Fact]
    public void The_same_monitor_is_the_windows()
    {
        MonitorPlacement.Pick(FullScreenOn.SameMonitor, Three[1], Three, here: 0).ShouldBe(0);
    }

    [Fact]
    public void Another_monitor_is_the_leftmost_the_window_is_not_on()
    {
        MonitorPlacement.Pick(FullScreenOn.OtherMonitor, null, Three, here: 0).ShouldBe(2);
        MonitorPlacement.Pick(FullScreenOn.OtherMonitor, null, Three, here: 2).ShouldBe(0);
    }

    [Fact]
    public void Another_monitor_with_only_one_is_the_windows()
    {
        MonitorPlacement.Pick(FullScreenOn.OtherMonitor, null, [Three[0]], here: 0).ShouldBe(0);
    }

    [Fact]
    public void The_chosen_monitor_is_found_wherever_the_window_is()
    {
        MonitorPlacement.Pick(FullScreenOn.ChosenMonitor, Monitor("right", 1920, 2560, 1440), Three, here: 2)
            .ShouldBe(1);
    }

    [Fact]
    public void A_chosen_monitor_that_moved_is_found_by_its_name()
    {
        MonitorPlacement.Pick(FullScreenOn.ChosenMonitor, Monitor("right", 5000, 2560, 1440), Three, here: 0)
            .ShouldBe(1);
    }

    [Fact]
    public void A_chosen_monitor_that_is_unplugged_is_the_windows()
    {
        MonitorPlacement.Pick(FullScreenOn.ChosenMonitor, Monitor("gone", 9000), Three, here: 2).ShouldBe(2);
    }
}
