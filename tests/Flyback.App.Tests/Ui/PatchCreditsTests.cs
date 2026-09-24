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
/// With nothing selected the panel shows who made the patch and its tags under
/// the description, and a double-click on either edits it.
/// </summary>
public class PatchCreditsTests : UiTest
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

    private static TextBlock Line(MainWindow window, string name) =>
        All<TextBlock>(window).Single(t => t.Name == name);

    private static TextBox? Box(MainWindow window) =>
        All<TextBox>(window).FirstOrDefault(t => t.Classes.Contains(ModulePlate.NameBoxClass));

    private static void Type(MainWindow window, string name, string text)
    {
        var line = Line(window, name);

        var at = line.TranslatePoint(new Point(10, line.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException($"{name} is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        var box = Box(window).ShouldNotBeNull($"{name} should have become a box");
        box.Text = text;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);
    }

    [AvaloniaFact]
    public void An_uncredited_patch_asks_who_made_it_and_for_tags()
    {
        var window = Open();

        Line(window, "patch-author").Text.ShouldBe("Double-click to say who made it.");
        Line(window, "patch-tags").Text.ShouldBe("Double-click to tag it.");
    }

    [AvaloniaFact]
    public void What_is_typed_credits_the_patch()
    {
        var window = Open();

        Type(window, "patch-author", "Ada");

        Editor(window).History.Patch.Author.ShouldBe("Ada");
        Line(window, "patch-author").Text.ShouldBe("by Ada");
    }

    [AvaloniaFact]
    public void Tags_are_typed_as_words_apart_by_spaces_or_commas()
    {
        var window = Open();

        Type(window, "patch-tags", "Drone, slow  ambient,drone");

        Editor(window).History.Patch.Tags.ShouldBe(["drone", "slow", "ambient"]);
        Line(window, "patch-tags").Text.ShouldBe("drone, slow, ambient");
    }

    [AvaloniaFact]
    public void Tagging_it_is_a_step_undo_takes_back()
    {
        var window = Open();

        Type(window, "patch-tags", "drone");
        Editor(window).History.Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).History.Patch.Tags.ShouldBeNull();
    }

    /// <summary>On a canvas the text owns, the panel writes the lines into the text.</summary>
    [AvaloniaFact]
    public void Crediting_a_patch_the_text_owns_writes_the_lines_into_the_text()
    {
        var window = Open();

        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        var text = All<TextEditor>(window).Single(e => e.Name == "source");
        text.Text = "description \"A hum.\"\n\nlet hum = t |> sine(freq: 220)\nhum |> out.left\n";
        All<Button>(window).Single(b => b.Name == "apply")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = false;
        Editor(window).Selection.Select(null);
        Settle(window);

        Type(window, "patch-author", "Ada");
        Type(window, "patch-tags", "drone slow");

        text.Text.ShouldStartWith("description \"A hum.\"\nauthor \"Ada\"\ntags \"drone\" \"slow\"\n\n");
        Editor(window).History.Patch.Author.ShouldBe("Ada");
        Editor(window).History.Patch.Tags.ShouldBe(["drone", "slow"]);
    }
}
