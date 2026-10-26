using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// Modules over the picture: their fills let it through, their text does not, and
/// the module whose value is in hand keeps its fill solid. A red window stands in
/// for the picture, so how much of it shows through is how red a pixel is.
/// </summary>
public class GlassModulesTests : EditorTest
{
    private (NodeEditor Editor, Window Window, NodeInstance Sine) OverRed()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = builder.Add(NodeCatalog.SineTypeId, 300, 200);
        var (editor, window) = Editing(builder.Patch);

        window.Background = Brushes.Red;
        editor.SeeThrough = true;
        Settle(window);

        return (editor, window, sine);
    }

    private static Rect Title(NodeInstance node) => new(node.X + 4, node.Y + 2, NodeGeometry.Width / 2, NodeGeometry.HeaderHeight - 4);

    /// <summary>A strip of body under the sockets' labels, with nothing written on it.</summary>
    private static Rect Bare(NodeInstance node, NodeEditor editor)
    {
        var bounds = editor.Geometry.Bounds(node, NodeCatalog.Get(node.TypeId)!);

        return new Rect(bounds.X + 30, bounds.Bottom - 4, 20, 2);
    }

    private static int Reddest(IEnumerable<Color> pixels) => pixels.Max(c => c.R - c.G);

    [AvaloniaFact]
    public void Over_the_picture_a_module_title_is_written_at_full_strength()
    {
        var (editor, window, sine) = OverRed();

        Pixels(editor, window, Title(sine)).Max(c => Math.Min(c.R, Math.Min(c.G, c.B))).ShouldBeGreaterThan((byte)230, "white text with nothing of the red behind it");
    }

    [AvaloniaFact]
    public void Over_the_picture_a_module_fill_lets_the_picture_through()
    {
        var (editor, window, sine) = OverRed();

        Reddest(Pixels(editor, window, Bare(sine, editor))).ShouldBeGreaterThan(30);
    }

    [AvaloniaFact]
    public void The_module_whose_value_is_in_hand_keeps_its_fill_solid()
    {
        var (editor, window, sine) = OverRed();
        var freq = NodeCatalog.Get(sine.TypeId)!.Inputs.ToList().FindIndex(p => p.Name == "freq");

        editor.WellFocus.Pick(new Flyback.Editor.Notices.SocketPick(sine.Id, freq));
        Settle(window);

        Reddest(Pixels(editor, window, Bare(sine, editor))).ShouldBeLessThan(10);
    }
}
