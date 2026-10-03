using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using Flyback.Editor.Inspect;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// With nothing selected the panel shows who made the patch and its tags under
/// the description. A double-click on the credit edits it, and a click on a tag
/// chip or the one to add another edits the tags.
/// </summary>
public class PatchCreditsTests : EditorTest
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

    private static TextBox NewTagBox(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "patch-tag-add").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        return All<TextBox>(window).Single(t => t.Name == "patch-tag-new");
    }

    private static void Press(MainWindow window, TextBox box, Key key)
    {
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Settle(window);
    }

    private static void TypeTags(MainWindow window, string text)
    {
        var box = NewTagBox(window);
        box.Text = text;
        Press(window, box, Key.Enter);
    }

    private static string[] Chips(MainWindow window) =>
        All<WrapPanel>(window).Single(p => p.Name == "patch-tags").Children
            .OfType<Border>().Where(b => b.Child is StackPanel)
            .Select(b => ((TextBlock)((StackPanel)b.Child!).Children[0]).Text!).ToArray();

    [AvaloniaFact]
    public void An_uncredited_patch_asks_who_made_it_and_for_tags()
    {
        var window = Open();

        Line(window, "patch-author").Text.ShouldBe("Double-click to say who made it.");
        Chips(window).ShouldBeEmpty();
        All<Button>(window).ShouldContain(b => b.Name == "patch-tag-add");
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
    public void Tags_are_typed_apart_by_commas_and_a_space_joins_a_tag()
    {
        var window = Open();

        TypeTags(window, "Drone, slow  ambient,drone");

        Editor(window).History.Patch.Tags.ShouldBe(["drone", "slow-ambient"]);
        Chips(window).ShouldBe(["drone", "slow-ambient"]);
    }

    [AvaloniaFact]
    public void Tagging_it_is_a_step_undo_takes_back()
    {
        var window = Open();

        TypeTags(window, "drone");
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
        TypeTags(window, "drone, slow");

        text.Text.ShouldStartWith("description \"A hum.\"\nauthor \"Ada\"\ntags \"drone\" \"slow\"\n\n");
        Editor(window).History.Patch.Author.ShouldBe("Ada");
        Editor(window).History.Patch.Tags.ShouldBe(["drone", "slow"]);
    }

    [AvaloniaFact]
    public void A_tag_typed_is_added_to_the_ones_there_are()
    {
        var window = Open();

        TypeTags(window, "drone");
        TypeTags(window, "slow");

        Chips(window).ShouldBe(["drone", "slow"]);
    }

    [AvaloniaFact]
    public void The_cross_on_a_chip_takes_that_tag_away()
    {
        var window = Open();

        TypeTags(window, "drone, slow, ambient");
        All<Button>(window).Single(b => b.Name == "remove-tag-slow").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Chips(window).ShouldBe(["drone", "ambient"]);
        Editor(window).History.Patch.Tags.ShouldBe(["drone", "ambient"]);
    }

    [AvaloniaFact]
    public void Taking_the_last_tag_away_leaves_the_patch_untagged_and_undo_puts_it_back()
    {
        var window = Open();

        TypeTags(window, "drone");
        All<Button>(window).Single(b => b.Name == "remove-tag-drone").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Editor(window).History.Patch.Tags.ShouldBeNull();

        Editor(window).History.Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).History.Patch.Tags.ShouldBe(["drone"]);
    }

    [AvaloniaFact]
    public void Escape_drops_what_was_typed_for_a_tag()
    {
        var window = Open();

        var box = NewTagBox(window);
        box.Text = "drone";
        Press(window, box, Key.Escape);

        Editor(window).History.Patch.Tags.ShouldBeNull();
        All<TextBox>(window).ShouldNotContain(t => t.Name == "patch-tag-new");
    }

    [AvaloniaFact]
    public void An_empty_box_adds_nothing_and_puts_the_chip_back()
    {
        var window = Open();

        var box = NewTagBox(window);
        Press(window, box, Key.Enter);

        Editor(window).History.Patch.Tags.ShouldBeNull();
        All<Button>(window).ShouldContain(b => b.Name == "patch-tag-add");
    }

    [AvaloniaFact]
    public void A_patch_with_all_its_tags_offers_no_more()
    {
        var window = Open();

        TypeTags(window, string.Join(',', Enumerable.Range(1, Patch.TagCount).Select(i => "t" + i)));

        All<Button>(window).ShouldNotContain(b => b.Name == "patch-tag-add");
    }
}
