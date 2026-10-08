using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Canvas;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// A patch opened is looked at whole, at the size the canvas settles at rather than the
/// first it is measured at, until somebody moves the view.
/// </summary>
public class FramingTests : EditorTest
{
    private static Patch Pair()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add("value", 0, 0);
        builder.Add(NodeCatalog.OutputTypeId, 520, 0);

        return builder.Patch;
    }

    /// <summary>The middle of what the patch covers, on screen.</summary>
    private static Point Middle(NodeEditor editor)
    {
        var on = editor.Selection.Scene.OnCanvas().Aggregate((a, b) => a.Union(b));

        return editor.View.OnScreen(on).Center;
    }

    [AvaloniaFact]
    public void A_patch_framed_at_a_passing_size_is_framed_again_at_the_one_it_settles_at()
    {
        var editor = NewCanvas(280, 265);
        var window = Show(editor, 280);

        editor.History.Open(Pair());
        Settle(window);

        editor.View.Resize(new Size(427, 757));

        Middle(editor).X.ShouldBe(427 / 2d, 1);
        Middle(editor).Y.ShouldBe(757 / 2d, 1);
    }

    [AvaloniaFact]
    public void A_view_somebody_moved_stays_where_they_left_it_when_the_canvas_grows()
    {
        var editor = NewCanvas(600, 400);
        var window = Show(editor, 600);

        editor.History.Open(Pair());
        Settle(window);
        editor.View.PanBy(new Vector(-40, 0));

        var pan = editor.View.Pan;
        editor.View.Resize(new Size(700, 400));

        editor.View.Pan.ShouldBe(pan);
    }
}
