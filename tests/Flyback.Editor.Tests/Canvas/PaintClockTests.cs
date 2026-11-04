using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>A script reads how long the canvas takes to draw, and the slowest draw since it last asked.</summary>
public class PaintClockTests : EditorTest
{
    [AvaloniaFact]
    public void The_readout_says_how_long_the_canvas_took_to_draw()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        var readout = Service<EditorReadout>(window);

        Service<NodeEditor>(window).InvalidateVisual();
        Settle(window);
        window.CaptureRenderedFrame()?.Dispose();

        var read = readout.Read();

        read["canvasPaints"]!.GetValue<int>().ShouldBeGreaterThan(0);
        read["canvasSlowestPaintMilliseconds"]!.GetValue<double>().ShouldBeGreaterThanOrEqualTo(read["canvasPaintMilliseconds"]!.GetValue<double>());
    }

    [AvaloniaFact]
    public void A_read_starts_the_count_again()
    {
        var clock = new PaintClock();

        clock.Record(TimeSpan.FromMilliseconds(30));
        clock.Record(TimeSpan.FromMilliseconds(5));

        clock.Take().ShouldBe((TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(30), 2));
        clock.Take().ShouldBe((TimeSpan.FromMilliseconds(5), TimeSpan.Zero, 0));
    }
}
