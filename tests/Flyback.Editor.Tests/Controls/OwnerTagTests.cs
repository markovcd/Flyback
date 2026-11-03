using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>
/// The tag in the canvas's corner says when the text owns the patch (ADR-0068), and
/// pressing it opens the text.
/// </summary>
public class OwnerTagTests : SourceViewTest
{
    private OwnerTag Tag(MainWindow window) => Service<OwnerTag>(window);

    private const string Sine = "sine(freq: 220) |> out.left";

    /// <summary>Applies text, then shows the canvas it built.</summary>
    private MainWindow OwnedByText()
    {
        var window = Open();

        Evaluate(window, Sine);
        CodeButton(window).IsChecked = false;
        Settle(window);

        return window;
    }

    [AvaloniaFact]
    public void A_canvas_that_owns_its_patch_shows_no_tag()
    {
        var window = Open();

        Tag(window).Box.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_patch_the_text_owns_shows_the_tag()
    {
        var window = OwnedByText();

        Tag(window).Box.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Pressing_the_tag_opens_the_text()
    {
        var window = OwnedByText();
        var editor = Editor(window);
        var at = editor.TranslatePoint(Tag(window).Box.ShouldNotBeNull().Center, window).ShouldNotBeNull();

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        CodeButton(window).IsChecked.ShouldBe(true);
        Text(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Pressing_the_tag_selects_nothing_under_it()
    {
        var window = OwnedByText();
        var editor = Editor(window);
        var tag = Tag(window).Box.ShouldNotBeNull();

        var under = editor.View.ToGraph(tag.Center);
        var node = editor.History.Patch.Nodes.First();

        node.X = under.X - NodeGeometry.Width / 2;
        node.Y = under.Y - NodeGeometry.HeaderHeight / 2;
        Settle(window);
        editor.Selection.Scene.HitNode(under)?.Id.ShouldBe(node.Id, "the module is under the tag");

        var at = editor.TranslatePoint(tag.Center, window).ShouldNotBeNull();

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        editor.Selection.Ids.ShouldNotContain(node.Id);
    }

    [AvaloniaFact]
    public void Handing_the_patch_back_takes_the_tag_down()
    {
        var window = Open();

        Evaluate(window, Sine);
        HandBack(window);

        Tag(window).Box.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Hovering_the_tag_explains_it()
    {
        var window = OwnedByText();
        var editor = Editor(window);
        var at = editor.TranslatePoint(Tag(window).Box.ShouldNotBeNull().Center, window).ShouldNotBeNull();

        window.MouseMove(at);
        Settle(window);

        Avalonia.Controls.ToolTip.GetTip(editor).ShouldBe(OwnerTag.Tip);
    }
}
