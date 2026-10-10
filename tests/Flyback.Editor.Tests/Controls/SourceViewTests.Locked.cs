using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Editor.Assist;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

public partial class SourceViewTests
{
    // --- a locked canvas, and a box on it --------------------------------------

    /// <summary>
    /// A double-click looks into a box rather than opening it, so a locked canvas does
    /// it too: nothing in the patch changes.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_looks_into_a_box_on_a_double_click()
    {
        var window = Open();

        Evaluate(window, """
            group "Voice" {
              let a = t |> sine(freq: 220)
              let b = a |> mul(a: _, b: 0.5)
            }
            b |> out.left
            """);

        var editor = Editor(window);

        editor.History.Locked.ShouldBeTrue();

        CodeButton(window).IsChecked = false;
        Settle(window);

        var group = editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        group.Collapsed.ShouldBeTrue("a group built from text arrives shut");

        var box = Geometry.GroupBounds(editor.History.Patch, group, editor.History.Patch.SocketsOf(group));

        var at = editor.TranslatePoint(
                editor.GraphToScreen.Transform(new Point(box.X + (box.Width / 2), box.Y + 8)), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        var steps = 0;
        editor.History.Recorded += (_, _) => steps++;

        for (var click = 0; click < 2; click++)
        {
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
        }

        Settle(window);

        group.Collapsed.ShouldBeTrue();
        steps.ShouldBe(0, "nothing done to a locked canvas is an edit to the patch");
        editor.Selection.Peeked.ShouldBe(group);
        editor.Selection.Count.ShouldBe(0, "looking into a box selects nothing inside it");
    }
}
