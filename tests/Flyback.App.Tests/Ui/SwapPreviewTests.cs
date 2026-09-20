using Avalonia;
using Avalonia.Controls;
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
    private MainWindow Open()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        return window;
    }

    private static ToggleButton Swap(MainWindow window) =>
        All<ToggleButton>(window).Single(b => b.Name == "swap");

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static void Press(MainWindow window, ToggleButton button)
    {
        button.IsChecked = !button.IsChecked;
        Settle(window);
    }

    /// <summary>Takes the wire off the Output's 'color', and hands back what it was.</summary>
    private static Connection Unwire(MainWindow window)
    {
        var editor = Editor(window);
        var output = editor.Patch.Output;

        var color = editor.Patch.IncomingTo(output.Id, NodeCatalog.OutputColorPort).ShouldNotBeNull();

        editor.Patch.Disconnect(output.Id, NodeCatalog.OutputColorPort);
        editor.NotifyPatchChanged();
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
        var output = editor.Patch.Output;
        editor.Patch.Connect(color.SourceNode, color.SourcePort, output.Id, NodeCatalog.OutputColorPort);
        editor.NotifyPatchChanged();
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
        editor.FrameAll();
        Settle(window);

        var swappedWidth = editor.Bounds.Width;

        var output = editor.Patch.Output;
        var socket = NodeGeometry.InputPort(
            output, NodeCatalog.BuiltIn.Require(output.TypeId), NodeCatalog.OutputColorPort);

        // Down on the socket and away over the Output's own body, where letting
        // go is a miss and the wire is simply gone.
        var body = new Point(output.X + NodeGeometry.Width / 2, output.Y + NodeGeometry.HeaderHeight / 2);

        window.MouseDown(OnWindow(window, socket), MouseButton.Left);
        window.MouseMove(OnWindow(window, body));
        Settle(window);

        editor.Patch.IncomingTo(output.Id, NodeCatalog.OutputColorPort)
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
