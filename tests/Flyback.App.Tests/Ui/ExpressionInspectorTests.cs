using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.App.Controls;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// An Expression's formula is a line on the panel to type into, and what is kept
/// there is both what the module computes and what it is called.
/// </summary>
public class ExpressionInspectorTests : UiTest
{
    private MainWindow Open(out NodeInstance expression, string? written = null)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var coord = b.Add(NodeCatalog.CoordTypeId, 40, 40);
        var formula = b.Add(NodeCatalog.ExpressionTypeId, 360, 40);

        if (written is not null) formula.SetState("expression", new JsonObject { ["formula"] = written });

        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(coord, 0, formula, 0).Wire(formula, 0, screen, NodeCatalog.OutputColorPort);

        var window = NewMainWindow();

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
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        All<TextBlock>(window).First(t => t.FontSize == 17);

    private static Color? Ink(MainWindow window) => (Formula(window).Foreground as ISolidColorBrush)?.Color;

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
        Title(window).Text.ShouldBe("Expression");
    }

    [AvaloniaFact]
    public void Enter_keeps_what_was_typed()
    {
        var window = Open(out var expression);

        Formula(window).Text = "fract(a * 3)";
        Settle(window);

        Held(expression).ShouldBe("a * b + c", "nothing is stored while it is being typed");

        Press(window, Key.Enter);

        Held(expression).ShouldBe("fract(a * 3)");
        Title(window).Text.ShouldBe("Expression", "the formula is not what it is called");
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
    /// A formula the module cannot read is marked, and says what stopped it: the
    /// module is giving 0 until it is corrected.
    /// </summary>
    [AvaloniaFact]
    public void A_formula_that_does_not_read_is_marked()
    {
        var window = Open(out _, "sin(");

        Ink(window).ShouldBe(Colors.Sink);
        ToolTip.GetTip(Formula(window)).ShouldBeOfType<string>().ShouldContain("it ends where a value was expected");
    }

    [AvaloniaFact]
    public void Keeping_one_that_reads_takes_the_mark_off()
    {
        var window = Open(out var expression, "sin(");

        Formula(window).Text = "sin(a)";
        Press(window, Key.Enter);

        Held(expression).ShouldBe("sin(a)");
        Ink(window).ShouldNotBe(Colors.Sink);
        ToolTip.GetTip(Formula(window)).ShouldBeNull();
    }

    /// <summary>
    /// What is half typed is not marked: the mark says what the module computes,
    /// and that is the last formula kept.
    /// </summary>
    [AvaloniaFact]
    public void One_that_reads_is_marked_only_once_an_unread_one_is_kept()
    {
        var window = Open(out _);

        Formula(window).Text = "sin(";
        Settle(window);

        Ink(window).ShouldNotBe(Colors.Sink);

        Press(window, Key.Enter);

        Ink(window).ShouldBe(Colors.Sink);
    }

    /// <summary>
    /// The mark reaches the screen. Skia is under the headless platform for this:
    /// a brush set on the box is not yet a brush drawn, the box's own template
    /// having a say in what it does with one.
    /// </summary>
    [AvaloniaFact]
    public void The_mark_is_drawn()
    {
        var window = Open(out _);

        Red(window).ShouldBe(0);

        Formula(window).Text = "sin(";
        Press(window, Key.Enter);

        Red(window).ShouldBeGreaterThan(0);
    }

    /// <summary>How many pixels of the box are drawn red.</summary>
    private static int Red(MainWindow window)
    {
        var box = Formula(window);

        // The panel is taller than the window, and what is past the bottom of it
        // is not in the frame at all.
        box.BringIntoView();
        Settle(window);

        var at = box.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("the box is not in this window");

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the window rendered nothing");

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var bgra = locked.Format == PixelFormat.Bgra8888;
        var count = 0;

        for (var y = (int)at.Y; y < Math.Min(at.Y + box.Bounds.Height, locked.Size.Height); y++)
        for (var x = (int)at.X; x < Math.Min(at.X + box.Bounds.Width, locked.Size.Width); x++)
        {
            var i = (y * locked.RowBytes) + (x * 4);

            var pixel = bgra
                ? Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i])
                : Color.FromRgb(bytes[i], bytes[i + 1], bytes[i + 2]);

            // That color and not merely a warm one: text is drawn a subpixel at
            // a time, so white letters have fringes redder than this test is.
            if (Math.Abs(pixel.R - Colors.Sink.R) <= 8
                && Math.Abs(pixel.G - Colors.Sink.G) <= 8
                && Math.Abs(pixel.B - Colors.Sink.B) <= 8)
            {
                count++;
            }
        }

        return count;
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
