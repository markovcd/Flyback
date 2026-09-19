using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// An Expression's formula is a line on the panel to type into, and what is kept
/// there is both what the module computes and what it is called.
/// </summary>
public class ExpressionInspectorTests : UiTest
{
    private static MainWindow Open(out NodeInstance expression)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var coord = b.Add(NodeCatalog.CoordTypeId, 40, 40);
        var formula = b.Add(NodeCatalog.ExpressionTypeId, 360, 40);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(coord, 0, formula, 0).Wire(formula, 0, screen, NodeCatalog.OutputColorPort);

        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).Patch = b.Patch;
        Settle(window);

        expression = Editor(window).Patch.Find(formula.Id)
            ?? throw new InvalidOperationException("the expression did not survive being opened");

        Select(window, expression);
        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static void Select(MainWindow window, NodeInstance node)
    {
        var editor = Editor(window);

        var body = new Point(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static TextBox Formula(MainWindow window) =>
        All<TextBox>(window).Single(t => t.MaxLength == ExtraField.Text.Limit);

    private static string Held(NodeInstance node) =>
        node.StateOf("expression")?["formula"]?.GetValue<string>() ?? string.Empty;

    private static TextBlock Title(MainWindow window) =>
        All<TextBlock>(window).First(t => t.FontSize == 17);

    private static void Press(MainWindow window, Key key)
    {
        Formula(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Settle(window);
    }

    [AvaloniaFact]
    public void The_panel_shows_the_formula_it_carries()
    {
        var window = Open(out _);

        Formula(window).Text.ShouldBe("a * b + c");
        Title(window).Text.ShouldBe("a * b + c");
    }

    [AvaloniaFact]
    public void Enter_keeps_what_was_typed_and_the_module_is_called_by_it()
    {
        var window = Open(out var expression);

        Formula(window).Text = "fract(a * 3)";
        Settle(window);

        Held(expression).ShouldBe("a * b + c", "nothing is stored while it is being typed");

        Press(window, Key.Enter);

        Held(expression).ShouldBe("fract(a * 3)");
        Title(window).Text.ShouldBe("fract(a * 3)");
        expression.Title(NodeCatalog.BuiltIn.Require(NodeCatalog.ExpressionTypeId)).ShouldBe("fract(a * 3)");
    }

    [AvaloniaFact]
    public void Escape_puts_back_what_was_there()
    {
        var window = Open(out var expression);

        Formula(window).Text = "sin(";
        Settle(window);
        Press(window, Key.Escape);

        Formula(window).Text.ShouldBe("a * b + c");
        Held(expression).ShouldBe("a * b + c");
    }
}
