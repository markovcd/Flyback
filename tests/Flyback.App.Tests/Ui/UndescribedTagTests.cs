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
/// A module the assistant is not told about says so: a tag on its header that
/// explains itself when hovered, and the same words at the foot of the inspector.
/// </summary>
public class UndescribedTagTests : UiTest
{
    private (MainWindow Window, NodeInstance Sine, NodeInstance Clock) Open()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var sine = b.Add("osc.sine", 360, 40);
        var clock = b.Add("time", 40, 40);

        b.Wire(sine, 0, output, NodeCatalog.OutputColorPort);

        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var editor = Editor(window);

        editor.Patch = b.Patch;
        editor.Undescribed = new HashSet<string> { "osc.sine" };
        Settle(window);

        return (window, sine, clock);
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    /// <summary>The middle of a module's tag, at the right of its header.</summary>
    private static Point Tag(NodeInstance node) => new(
        node.X + NodeGeometry.Width - 7 - 8,
        node.Y + NodeGeometry.HeaderHeight / 2);

    private static void Select(MainWindow window, NodeInstance node)
    {
        var at = OnWindow(window, new Point(node.X + 30, node.Y + NodeGeometry.HeaderHeight / 2));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static bool Noted(MainWindow window) =>
        All<TextBlock>(window).Any(t => t.Text == AssistantPanel.UndescribedNote);

    [AvaloniaFact]
    public void Hovering_the_tag_explains_it()
    {
        var (window, sine, _) = Open();
        var editor = Editor(window);

        window.MouseMove(OnWindow(window, Tag(sine)));
        Settle(window);

        ToolTip.GetTip(editor).ShouldBe(AssistantPanel.UndescribedNote);
        ToolTip.GetIsOpen(editor).ShouldBeTrue();

        window.MouseMove(OnWindow(window, new Point(sine.X + 30, sine.Y + 60)));
        Settle(window);

        ToolTip.GetIsOpen(editor).ShouldBeFalse("the pointer has left the tag");
        ToolTip.GetTip(editor).ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_module_that_is_described_has_no_tag_to_hover()
    {
        var (window, _, clock) = Open();

        window.MouseMove(OnWindow(window, Tag(clock)));
        Settle(window);

        ToolTip.GetTip(Editor(window)).ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_inspector_says_the_same_under_a_module_left_out()
    {
        var (window, sine, clock) = Open();

        Select(window, sine);
        Noted(window).ShouldBeTrue();

        Select(window, clock);
        Noted(window).ShouldBeFalse();
    }
}
