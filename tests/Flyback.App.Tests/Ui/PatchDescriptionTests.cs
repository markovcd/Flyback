using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// With nothing selected the panel shows what the patch is for, and a double-click
/// there edits it the way one on a module's name renames the module.
/// </summary>
public class PatchDescriptionTests : UiTest
{
    private MainWindow Open()
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var osc = b.Add(NodeCatalog.SineTypeId, 360, 40);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(osc, 0, screen, NodeCatalog.OutputColorPort);

        Editor(window).Patch = b.Patch;
        Settle(window);

        Editor(window).SelectedNode.ShouldBeNull();
        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static TextBlock Description(MainWindow window) =>
        All<TextBlock>(window).Single(t => t.Name == "patch-description");

    private static TextBox? Box(MainWindow window) =>
        All<TextBox>(window).FirstOrDefault(t => t.Classes.Contains(ModulePlate.NameBoxClass));

    private static void DoubleClickDescription(MainWindow window)
    {
        var description = Description(window);

        var at = description.TranslatePoint(new Point(10, description.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the description is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static void Describe(MainWindow window, string text)
    {
        DoubleClickDescription(window);

        var box = Box(window).ShouldNotBeNull("the description should have become a box");
        box.Text = text;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);
    }

    [AvaloniaFact]
    public void An_undescribed_patch_asks_for_a_description()
    {
        var window = Open();

        Description(window).Text.ShouldBe("Double-click to say what this patch is for.");
    }

    [AvaloniaFact]
    public void What_is_typed_describes_the_patch()
    {
        var window = Open();

        Describe(window, "A green hum.");

        Editor(window).Patch.Description.ShouldBe("A green hum.");
        Description(window).Text.ShouldBe("A green hum.");
        Box(window).ShouldBeNull();
    }

    [AvaloniaFact]
    public void Describing_it_is_a_step_undo_takes_back()
    {
        var window = Open();

        Describe(window, "A green hum.");
        Editor(window).Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).Patch.Description.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Emptying_the_box_takes_the_description_away()
    {
        var window = Open();

        Describe(window, "A green hum.");
        Describe(window, "  ");

        Editor(window).Patch.Description.ShouldBeNull();
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

        Editor(window).Locked.ShouldBeTrue();

        // Back on the canvas, with the caret's module let go of.
        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = false;
        Editor(window).Select(null);
        Settle(window);

        Describe(window, "A hum.");

        text.Text.ShouldStartWith("description \"A hum.\"");
        Editor(window).Patch.Description.ShouldBe("A hum.");
    }
}
