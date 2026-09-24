using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>The toolbar's "Compiling…": said only for a wait long enough to notice, and gone when it ends.</summary>
public class ShimmerTests : UiTest
{
    [AvaloniaFact]
    public void A_short_wait_is_never_shown()
    {
        var now = TimeSpan.Zero;
        var busy = true;
        var shimmer = new Shimmer("Compiling…") { Now = () => now };
        Show(shimmer);

        shimmer.Watch(() => busy);
        shimmer.IsVisible.ShouldBeFalse();

        now = Shimmer.Delay / 2;
        busy = false;
        shimmer.Tick();

        shimmer.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_long_wait_is_shown_until_it_ends()
    {
        var now = TimeSpan.Zero;
        var busy = true;
        var shimmer = new Shimmer("Compiling…") { Now = () => now };
        Show(shimmer);

        shimmer.Watch(() => busy);

        now = Shimmer.Delay * 2;
        shimmer.Tick();
        shimmer.IsVisible.ShouldBeTrue();

        busy = false;
        shimmer.Tick();
        shimmer.IsVisible.ShouldBeFalse();
    }
}
