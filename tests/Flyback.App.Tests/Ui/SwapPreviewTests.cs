using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The toolbar's swap: the picture in the wide column and the canvas where the
/// picture was, for as long as there is a picture.
/// </summary>
public class SwapPreviewTests : UiTest
{

    private static ToggleButton Swap(MainWindow window) =>
        All<ToggleButton>(window).Single(b => b.Name == "swap");

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static void Press(MainWindow window, ToggleButton button)
    {
        button.IsChecked = !button.IsChecked;
        Settle(window);
    }

    /// <summary>Takes the wire off the Output's 'color', and hands back what it was.</summary>
    private static Connection Unwire(MainWindow window)
    {
        var editor = Editor(window);
        var output = editor.History.Patch.Output;

        var color = editor.History.Patch.IncomingTo(output.Id, NodeCatalog.OutputColorPort).ShouldNotBeNull();

        editor.History.Patch.Disconnect(output.Id, NodeCatalog.OutputColorPort);
        editor.History.Record();
        Settle(window);

        return color;
    }

    [AvaloniaFact]
    public void Swapping_puts_the_picture_where_the_canvas_was_and_back()
    {
        var window = Open();

        var preview = Preview(window);
        var editor = Editor(window);

        var pictureWas = preview.Bounds.Width;
        var canvasWas = editor.Bounds.Width;

        pictureWas.ShouldBeLessThan(canvasWas, "the canvas starts in the wide column");

        Press(window, Swap(window));

        preview.Bounds.Width.ShouldBeGreaterThan(editor.Bounds.Width, "and the picture has it now");
        preview.Bounds.Height.ShouldBeGreaterThan(editor.Bounds.Height, "at the window's full height");
        editor.IsEffectivelyVisible.ShouldBeTrue("the canvas is still there to patch on");

        Press(window, Swap(window));

        preview.Bounds.Width.ShouldBe(pictureWas, 0.5);
        editor.Bounds.Width.ShouldBe(canvasWas, 0.5);
    }

    /// <summary>
    /// Only the picture and the canvas trade: the assistant stays leftmost, now
    /// beside the picture, and the knobs stay under whatever is in the wide column.
    /// </summary>
    [AvaloniaFact]
    public void The_assistant_and_the_knobs_keep_their_places()
    {
        var window = Open();

        All<ToggleButton>(window).Single(b => b.Name == "assistant").IsChecked = true;
        All<ToggleButton>(window).Single(b => b.Name == "controls").IsChecked = true;
        Settle(window);

        var assistant = All<AssistantPanel>(window).Single();
        var knobs = All<ControlsPanel>(window).Single();
        var editor = Editor(window);
        var preview = Preview(window);

        Press(window, Swap(window));

        Rect On(Visual visual) => new(
            visual.TranslatePoint(default, window) ?? throw new InvalidOperationException("not in this window"),
            visual.Bounds.Size);

        var (a, k, p, e) = (On(assistant), On(knobs), On(preview), On(editor));

        a.Left.ShouldBe(0, 1, "the assistant is still leftmost");
        a.Right.ShouldBeLessThanOrEqualTo(p.Left, "and beside the picture");
        k.Top.ShouldBeGreaterThanOrEqualTo(p.Bottom, "the knobs are under the picture");
        k.Left.ShouldBe(p.Left, 1);
        k.Width.ShouldBe(p.Width, 1);
        e.Left.ShouldBeGreaterThan(p.Right, "the canvas has the narrow column");

        Press(window, Swap(window));

        (a, k, p, e) = (On(assistant), On(knobs), On(preview), On(editor));

        a.Right.ShouldBeLessThanOrEqualTo(e.Left, "the assistant is beside the canvas again");
        k.Top.ShouldBeGreaterThanOrEqualTo(e.Bottom, "with the knobs under it");
        p.Left.ShouldBeGreaterThan(e.Right);
    }

    /// <summary>
    /// For the reason the full screen preview's tests give: moving the GPU surface
    /// to another parent would tear its context down.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_is_never_moved_to_get_there()
    {
        var window = Open();

        var preview = Preview(window);
        var parent = preview.GetVisualParent();
        var grandparent = parent?.GetVisualParent();

        Press(window, Swap(window));

        Preview(window).ShouldBeSameAs(preview);
        preview.GetVisualParent().ShouldBeSameAs(parent);
        parent?.GetVisualParent().ShouldBeSameAs(grandparent);
    }

    [AvaloniaFact]
    public void A_patch_with_no_picture_has_nothing_to_swap()
    {
        var window = Open();

        Swap(window).IsEnabled.ShouldBeTrue("the preset opened on draws something");

        Unwire(window);

        Swap(window).IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Losing_the_picture_while_swapped_puts_the_layout_back()
    {
        var window = Open();

        var editor = Editor(window);
        var canvasWas = editor.Bounds.Width;

        Press(window, Swap(window));
        editor.Bounds.Width.ShouldBeLessThan(canvasWas);

        var color = Unwire(window);

        Swap(window).IsChecked.ShouldBe(false);
        Swap(window).IsEnabled.ShouldBeFalse();
        editor.Bounds.Width.ShouldBe(canvasWas, 0.5, "the canvas has its column back");

        // And the picture coming back does not swap it again by itself.
        var output = editor.History.Patch.Output;
        editor.History.Patch.Connect(color.SourceNode, color.SourcePort, output.Id, NodeCatalog.OutputColorPort);
        editor.History.Record();
        Settle(window);

        Swap(window).IsEnabled.ShouldBeTrue();
        Swap(window).IsChecked.ShouldBe(false);
        editor.Bounds.Width.ShouldBe(canvasWas, 0.5);
    }

    /// <summary>A point in graph space, in the window's own coordinates.</summary>
    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    /// <summary>
    /// Pulling the wire off 'color' on the swapped canvas takes the picture away
    /// at the press, but the canvas stays under the hand until the button is up.
    /// </summary>
    [AvaloniaFact]
    public void Unplugging_color_on_the_canvas_swaps_back_only_once_the_button_is_up()
    {
        var window = Open();
        var editor = Editor(window);

        Press(window, Swap(window));

        // Framed in the narrow cell, so the Output's socket is somewhere a
        // pointer can reach.
        editor.View.FrameAll();
        Settle(window);

        var swappedWidth = editor.Bounds.Width;

        var output = editor.History.Patch.Output;
        var socket = Geometry.InputPort(
            output, NodeCatalog.BuiltIn.Require(output.TypeId), NodeCatalog.OutputColorPort);

        // Down on the socket and away over the Output's own body, where letting
        // go is a miss and the wire is simply gone.
        var body = new Point(output.X + NodeGeometry.Width / 2, output.Y + NodeGeometry.HeaderHeight / 2);

        window.MouseDown(OnWindow(window, socket), MouseButton.Left);
        window.MouseMove(OnWindow(window, body));
        Settle(window);

        editor.History.Patch.IncomingTo(output.Id, NodeCatalog.OutputColorPort)
            .ShouldBeNull("the wire comes off at the press");
        Swap(window).IsChecked.ShouldBe(true, "but nothing moves while the wire is held");
        editor.Bounds.Width.ShouldBe(swappedWidth, 0.5);

        window.MouseUp(OnWindow(window, body), MouseButton.Left);
        Settle(window);

        Swap(window).IsChecked.ShouldBe(false);
        Swap(window).IsEnabled.ShouldBeFalse();
        editor.Bounds.Width.ShouldBeGreaterThan(swappedWidth, "the canvas has its column back");
    }
}
