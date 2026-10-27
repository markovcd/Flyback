using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>Over a live picture a module has a dark line outside its edge and a light one inside, so it stands out against any color.</summary>
public class GlassEdgeTests : EditorTest
{
    [AvaloniaFact]
    public void Over_the_picture_a_module_edge_is_dark_outside_and_light_inside()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = builder.Add(NodeCatalog.SineTypeId, 300, 200);
        var (editor, window) = Editing(builder.Patch);
        var bounds = editor.Geometry.Bounds(sine, NodeCatalog.Get(sine.TypeId)!);
        var across = bounds.Center.X - 40;

        // Along the bottom edge, where no socket sits.
        int Brightness(double y) => Pixels(editor, window, new Rect(across, y, 20, 1)).Sum(c => c.R + c.G + c.B);

        var insideBefore = Brightness(bounds.Bottom - 2);

        editor.SeeThrough = true;
        Settle(window);

        Brightness(bounds.Bottom - 2).ShouldBeGreaterThan(insideBefore, "a light line just inside the edge");
        Pixels(editor, window, new Rect(across, bounds.Bottom + 0.5, 20, 1)).ShouldAllBe(c => c.R + c.G + c.B < 60, "a dark line just outside it");
        Shoot(window, "glass-edge.png");
    }
}
