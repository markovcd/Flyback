using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What happens to a gesture when the patch it is holding on to moves under it.
/// </summary>
/// <remarks>
/// The pointer is captured for the length of a drag and the keyboard is not, so
/// Ctrl+Z arrives in the middle of one perfectly well — and an assistant
/// answering is not waiting for anybody's hand either. Both replace the patch,
/// and a drag is holding pieces of the one that was there: the module under the
/// pointer, or the far end of the wire being drawn.
/// </remarks>
public class MidDragTests : UiTest
{
    private const string Sine = "osc.sine";
    private const string Add = "math.add";

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    private static Point Input(NodeInstance node, int index) =>
        NodeGeometry.InputPort(node, NodeCatalog.BuiltIn.Require(node.TypeId), index);

    private static Point Output(NodeInstance node, int index = 0) =>
        NodeGeometry.OutputPort(node, index);

    private static Point Body(NodeInstance node) =>
        new(node.X + (NodeGeometry.Width / 2), node.Y + 10);

    private static void Press(MainWindow window, string named) =>
        All<Button>(window).Single(b => b.Name == named)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>An oscillator, something to feed, and the Output they need.</summary>
    private static (MainWindow Window, NodeInstance Source, NodeInstance Fed) Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var source = b.Add(Sine, 40, 40);
        var fed = b.Add(Add, 400, 40);

        Editor(window).Patch = b.Patch;
        Settle(window);

        return (window, source, fed);
    }

    /// <summary>
    /// Undo waits for the hand to come off.
    /// </summary>
    /// <remarks>
    /// What it would take back is the patch the drag is holding on to, and the
    /// step it lands on need never have had the module under the pointer. The
    /// press is ignored rather than answered: letting go finishes the gesture
    /// and leaves it to be made again.
    /// </remarks>
    [AvaloniaFact]
    public void Undo_is_not_answered_while_a_drag_is_under_way()
    {
        var (window, source, fed) = Open();
        var editor = Editor(window);

        // Something to take back, so a press that worked would show.
        editor.Patch.Connect(source.Id, 0, fed.Id, 0);
        editor.NotifyPatchChanged();
        Settle(window);

        editor.CanUndo.ShouldBeTrue();

        window.MouseDown(OnWindow(window, Body(source)), MouseButton.Left);
        window.MouseMove(OnWindow(window, new Point(source.X + 120, source.Y + 60)));
        Settle(window);

        editor.Gesturing.ShouldBeTrue("the module is being dragged");

        Press(window, "undo");
        Settle(window);

        editor.Patch.Connections.Count.ShouldBe(1, "the wire is still there, so nothing was taken back");

        window.MouseUp(OnWindow(window, new Point(source.X + 120, source.Y + 60)), MouseButton.Left);
        Settle(window);

        // And the press works again the moment the hand is off, so this defers
        // the gesture rather than losing it.
        Press(window, "undo");
        Settle(window);

        Press(window, "undo");
        Settle(window);

        editor.Patch.Connections.ShouldBeEmpty("the move came back, and the wire behind it");
    }

    /// <summary>
    /// A patch arriving from elsewhere abandons the gesture, which is what makes
    /// the press above the only way in.
    /// </summary>
    /// <remarks>
    /// The assistant does not wait for anybody's hand to be still, and a wire
    /// being drawn is holding a module by its id. Showing a patch drops the drag
    /// rather than letting it finish against a canvas it was not started on, so
    /// the wire is never completed to a module that has gone.
    /// </remarks>
    [AvaloniaFact]
    public void A_patch_arriving_mid_drag_drops_the_gesture()
    {
        var (window, source, _) = Open();
        var editor = Editor(window);

        window.MouseDown(OnWindow(window, Output(source)), MouseButton.Left);
        window.MouseMove(OnWindow(window, new Point(source.X + 200, source.Y + 40)));
        Settle(window);

        editor.Gesturing.ShouldBeTrue("a wire is being drawn");

        // The assistant answers with a patch this oscillator is not in.
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var fed = b.Add(Add, 400, 40);

        editor.ApplyEdit(b.Patch);
        Settle(window);

        editor.Gesturing.ShouldBeFalse("the patch it was started on is not there any more");
        editor.Patch.Find(source.Id).ShouldBeNull("nor is the end it was holding");

        // Letting go over a socket does nothing, so no wire names the module
        // that has gone. Measured after the patch arrived, because showing it
        // framed the view afresh.
        window.MouseUp(OnWindow(window, Input(fed, 0)), MouseButton.Left);
        Settle(window);

        editor.Patch.Connections.ShouldBeEmpty();
    }
}
