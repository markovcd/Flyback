using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>An input the module's formula never reads is drawn faint, as nothing set on it does anything.</summary>
public class IdleInputTests : EditorTest
{
    [AvaloniaFact]
    public void An_input_the_formula_does_not_read_is_drawn_fainter_than_one_it_reads()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var expression = builder.Add(NodeCatalog.ExpressionTypeId, 300, 200);
        expression.SetState("expression", new JsonObject { ["formula"] = "a * 0.11" });
        var (editor, window) = Editing(builder.Patch);
        var def = NodeCatalog.Get(expression.TypeId)!;

        Rect Label(int port)
        {
            var center = editor.Geometry.InputPort(expression, def, port);

            return new Rect(center.X + 8, center.Y - 5, 20, 10);
        }

        int Brightest(int port) => Pixels(editor, window, Label(port)).Max(c => c.R + c.G + c.B);

        CanvasPainter.Idle(NodeCatalog.FormulaOf(expression), 1).ShouldBeTrue();
        Brightest(1).ShouldBeLessThan(Brightest(0) - 150, "b's label is faint beside a's");
        Shoot(window, "idle-inputs.png");
    }
}
