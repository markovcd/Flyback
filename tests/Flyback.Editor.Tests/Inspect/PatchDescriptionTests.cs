using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Input;
using AvaloniaEdit;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>
/// With nothing selected the panel shows what the patch is for, and a click there
/// opens a box with Save and Cancel.
/// </summary>
public class PatchDescriptionTests : EditorTest
{
    private MainWindow Open()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var osc = b.Add(NodeCatalog.SineTypeId, 360, 40);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(osc, 0, screen, NodeCatalog.OutputColorPort);

        var window = Open(b.Patch);

        Editor(window).Selection.Focused.ShouldBeNull();
        return window;
    }

    private static TextBlock Description(MainWindow window) =>
        All<TextBlock>(window).Single(t => t.Name == "patch-description");

    private static TextBox? Box(MainWindow window) =>
        All<TextBox>(window).FirstOrDefault(t => t.Name == "patch-description-box");

    private static void Click(MainWindow window, string name)
    {
        All<Button>(window).Single(b => b.Name == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    private static TextBox StartEditing(MainWindow window)
    {
        Click(window, "patch-description-button");

        return Box(window).ShouldNotBeNull("the description should have become a box");
    }

    private static void Describe(MainWindow window, string text)
    {
        var box = StartEditing(window);
        box.Text = text;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);
    }

    [AvaloniaFact]
    public void An_undescribed_patch_asks_for_a_description()
    {
        var window = Open();

        Description(window).Text.ShouldBe("Add a description");
    }

    [AvaloniaFact]
    public void What_is_typed_describes_the_patch()
    {
        var window = Open();

        Describe(window, "A green hum.");

        Editor(window).History.Patch.Description.ShouldBe("A green hum.");
        Description(window).Text.ShouldBe("A green hum.");
        Box(window).ShouldBeNull();
    }

    [AvaloniaFact]
    public void Save_keeps_what_was_typed_and_Cancel_and_Escape_drop_it()
    {
        var window = Open();

        var box = StartEditing(window);
        box.Text = "A green hum.";
        Click(window, "patch-description-save");
        Editor(window).History.Patch.Description.ShouldBe("A green hum.");

        box = StartEditing(window);
        box.Text = "Something else.";
        Click(window, "patch-description-cancel");
        Editor(window).History.Patch.Description.ShouldBe("A green hum.");
        Box(window).ShouldBeNull();

        box = StartEditing(window);
        box.Text = "Something else.";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Settle(window);
        Editor(window).History.Patch.Description.ShouldBe("A green hum.");
    }

    [AvaloniaFact]
    public void The_text_stays_where_it_was_when_the_box_opens()
    {
        var window = Open();

        Describe(window, "Three drifting sine fields summed and read through a cosine palette, the hello world of video synths.");

        var shown = Description(window);
        var before = shown.TranslatePoint(new Point(0, 0), window)!.Value;
        var lines = shown.Bounds.Height;
        var width = All<Button>(window).Single(b => b.Name == "patch-description-button").Bounds.Width - 20;

        var box = StartEditing(window);
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().Single();
        var after = presenter.TranslatePoint(new Point(0, 0), window)!.Value;

        after.X.ShouldBe(before.X, 0.5);
        after.Y.ShouldBe(before.Y, 0.5);
        presenter.Bounds.Width.ShouldBe(width, 0.5);
        presenter.Bounds.Height.ShouldBeGreaterThanOrEqualTo(lines - 0.5);
    }

    [AvaloniaFact]
    public void Leaving_the_box_keeps_what_was_typed()
    {
        var window = Open();

        var box = StartEditing(window);
        box.Text = "A green hum.";

        Editor(window).Selection.Select(Editor(window).History.Patch.Nodes[0].Id);
        Settle(window);

        Editor(window).History.Patch.Description.ShouldBe("A green hum.");
    }

    [AvaloniaFact]
    public void Describing_it_is_a_step_undo_takes_back()
    {
        var window = Open();

        Describe(window, "A green hum.");
        Editor(window).History.Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).History.Patch.Description.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Emptying_the_box_takes_the_description_away()
    {
        var window = Open();

        Describe(window, "A green hum.");
        Describe(window, "  ");

        Editor(window).History.Patch.Description.ShouldBeNull();
    }

    /// <summary>On a canvas the text owns, the panel writes the line into the text.</summary>
    [AvaloniaFact]
    public void Describing_a_patch_the_text_owns_writes_the_line_into_the_text()
    {
        var window = Open();

        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        var text = All<TextEditor>(window).Single(e => e.Name == "source");
        text.Text = "let hum = t |> sine(freq: 220)\nhum |> out.left\n";
        All<Button>(window).Single(b => b.Name == "apply")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Editor(window).History.Locked.ShouldBeTrue();

        // Back on the canvas, with the caret's module let go of.
        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = false;
        Editor(window).Selection.Select(null);
        Settle(window);

        Describe(window, "A hum.");

        text.Text.ShouldStartWith("description \"A hum.\"");
        Editor(window).History.Patch.Description.ShouldBe("A hum.");
    }
}
