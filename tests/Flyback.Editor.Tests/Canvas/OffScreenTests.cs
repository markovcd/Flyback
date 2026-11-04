using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// What lies wholly off the view is not drawn, and what overhangs its edge still is.
/// </summary>
public class OffScreenTests : EditorTest
{
    private const double Wide = 400, Tall = 800;

    private Viewport View()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add("value", 0, 0);

        var (editor, _) = Editing(builder.Patch, Wide, Tall);

        return editor.View;
    }

    [AvaloniaFact]
    public void A_module_across_the_edge_of_the_view_is_drawn()
    {
        var view = View();
        var seen = view.Visible;

        view.Shows(new Rect(seen.Right - 1, seen.Top, NodeGeometry.Width, 100)).ShouldBeTrue();
        view.Shows(new Rect(seen.Left - NodeGeometry.Width + 1, seen.Top, NodeGeometry.Width, 100)).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_module_just_past_the_edge_is_drawn_for_what_overhangs_it()
    {
        var view = View();
        var seen = view.Visible;

        view.Shows(new Rect(seen.Right + 1, seen.Top, NodeGeometry.Width, 100)).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_module_far_off_the_view_is_not_drawn()
    {
        var view = View();
        var seen = view.Visible;

        view.Shows(new Rect(seen.Right + seen.Width * 4 + NodeGeometry.Width * 4, seen.Top, NodeGeometry.Width, 100)).ShouldBeFalse();
        view.Shows(new Rect(seen.Left, seen.Bottom + seen.Height * 4 + 2000, NodeGeometry.Width, 100)).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_level_wire_across_the_view_is_drawn()
    {
        var view = View();
        var middle = view.Visible.Center;

        view.Shows(WirePath.Extent(middle - new Vector(50, 0), middle + new Vector(50, 0))).ShouldBeTrue();
    }

    [AvaloniaTheory]
    [InlineData(0, 0, 400, 120)]
    [InlineData(0, 0, 20, -300)]
    [InlineData(100, 50, 101, 50)]
    public void A_wire_stays_inside_the_extent_it_is_culled_by(double x0, double y0, double x1, double y1)
    {
        var (from, to) = (new Point(x0, y0), new Point(x1, y1));
        var extent = WirePath.Extent(from, to).Inflate(0.001);

        for (var t = 0.0; t <= 1; t += 0.01)
            extent.Contains(WirePath.At(from, to, t)).ShouldBeTrue($"t = {t}");
    }
}
