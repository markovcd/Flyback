using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Canvas;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// A locked canvas — the source view owning the edits (ADR-0068) — still lets a
/// module be dragged to see the patch more clearly, even though nothing dragged
/// there is written back into the text.
/// </summary>
public class LockedCanvasDragTests : EditorTest
{
    private static Patch Pair(out NodeInstance source, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        source = builder.Add("value", 0, 0);
        sink = builder.Add(NodeCatalog.OutputTypeId, 420, 0);

        builder.Wire(source, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    [AvaloniaFact]
    public void A_module_moves_on_a_locked_canvas()
    {
        var (editor, window) = Editing(Pair(out var source, out _));

        editor.History.Locked = true;

        Drag(editor, window, Body(source), Body(source) + new Vector(80, 40));

        source.X.ShouldBe(80);
        source.Y.ShouldBe(40);
    }

    [AvaloniaFact]
    public void Moving_a_module_on_a_locked_canvas_leaves_no_step_to_undo()
    {
        var (editor, window) = Editing(Pair(out var source, out _));

        editor.History.Locked = true;

        Drag(editor, window, Body(source), Body(source) + new Vector(80, 40));

        editor.History.CanUndo.ShouldBeFalse();
    }
}
