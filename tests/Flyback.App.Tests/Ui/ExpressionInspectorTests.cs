using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.App.Controls;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// An Expression's formula is a line on the panel to type into, and what is kept
/// there is both what the module computes and what it is called.
/// </summary>
public class ExpressionInspectorTests : UiTest
{
    private static MainWindow Open(out NodeInstance expression, string? written = null)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var coord = b.Add(NodeCatalog.CoordTypeId, 40, 40);
        var formula = b.Add(NodeCatalog.ExpressionTypeId, 360, 40);

        if (written is not null) formula.SetState("expression", new JsonObject { ["formula"] = written });

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

    /// <summary>
    /// A formula printed as the sum it is has no argument for the new one to be
    /// written into, so the printing is printed again rather than left saying the
    /// old sum.
    /// </summary>
    [AvaloniaFact]
    public void A_formula_edited_over_a_printing_is_printed_again()
    {
        var window = Open(out var expression, "a * 2");

        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        var text = All<TextEditor>(window).Single(e => e.Name == "source");

        text.Text.ShouldContain("x * 2 |> out.color");

        // The caret on the operator is the caret on the module the sum placed.
        text.CaretOffset = text.Text.IndexOf('*');
        Settle(window);

        All<NodeEditor>(window).Single().SelectedNode.ShouldNotBeNull().Id.ShouldBe(expression.Id);

        Formula(window).Text = "a * 3 - 1";
        Press(window, Key.Enter);

        Held(expression).ShouldBe("a * 3 - 1");
        text.Text.ShouldContain("x * 3 - 1 |> out.color");
        All<TextBlock>(window).ShouldNotContain(block => block.Text != null && block.Text.Contains("could not be written"));
    }
}
