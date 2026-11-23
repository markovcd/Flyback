using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>The split between the picture and the inspector stays where it was dragged while the patch is edited.</summary>
public class PictureSplitTests : EditorTest
{
    /// <summary>A Value module whose knob colors the picture.</summary>
    private static (Patch Patch, NodeInstance Value) Board()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(OutputModule.TypeId, 700, 40);
        var value = b.Add("value", 200, 40, (0, 0.2f));

        b.Wire(value, 0, output, OutputModule.ColorPort);

        return (b.Patch, value);
    }

    private static Point Well(MainWindow window, NodeInstance node, int port)
    {
        var editor = Editor(window);
        var def = NodeCatalog.Get(node.TypeId)!;
        var graph = ValueWell.At(Geometry.Bounds(node, def), Geometry.InputPort(node, def, port)).Center;

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)!.Value;
    }

    private static void Drag(MainWindow window, Point from, Vector by)
    {
        window.MouseDown(from, MouseButton.Left);
        for (var i = 1; i <= 4; i++) window.MouseMove(from + by * i / 4);
        window.MouseUp(from + by, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void Turning_a_value_on_the_canvas_keeps_the_dragged_split()
    {
        var (patch, value) = Board();
        var window = Open(patch);
        var grid = All<Grid>(window).Single(g => g.Name == "columns");
        var splitter = All<GridSplitter>(grid).Single(s => s.Name == "preview-splitter");
        var picture = grid.RowDefinitions[0];
        var before = picture.ActualHeight;

        Drag(window, MiddleOf(splitter, window), new Vector(0, -80));

        var dragged = picture.ActualHeight;
        dragged.ShouldBeLessThan(before - 40, "the splitter drag took");

        var was = value.InputValues[0];
        Drag(window, Well(window, value, 0), new Vector(0, -40));

        Editor(window).History.Patch.Find(value.Id)!.InputValues[0].ShouldNotBe(was, "the value was turned");
        picture.ActualHeight.ShouldBe(dragged, 1);
    }
}
