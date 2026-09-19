using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
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
    private static MainWindow Open()
    {
        var window = new MainWindow();

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
}
