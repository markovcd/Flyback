using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>
/// With nothing selected the panel shows who made the patch and its tags under
/// the description. The credit is a button to say who, then a card with a pencil
/// and a cross; tags are chips with a cross, and a dashed one to add another.
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

    private static Button Named(MainWindow window, string name) =>
        All<Button>(window).Single(b => b.Name == name);

    private static void Click(MainWindow window, string name)
    {
        Press(Named(window, name));
        Settle(window);
    }

    private static string? Credit(MainWindow window) =>
        All<TextBlock>(window).SingleOrDefault(t => t.Name == "patch-author")?.Text;

    private static TextBox AuthorBox(MainWindow window) =>
        All<TextBox>(window).Single(t => t.Name == "patch-author-box");

    private static void Say(MainWindow window, string author)
    {
        Click(window, "patch-author-add");

        var box = AuthorBox(window);
        box.Text = author;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);
    }

    private static TextBox NewTagBox(MainWindow window)
    {
        Press(All<Button>(window).Single(b => b.Name == "patch-tag-add"));
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

        Credit(window).ShouldBeNull();
        All<Button>(window).ShouldContain(b => b.Name == "patch-author-add");
        Chips(window).ShouldBeEmpty();
        All<Button>(window).ShouldContain(b => b.Name == "patch-tag-add");
    }

    [AvaloniaFact]
    public void What_is_typed_credits_the_patch()
    {
        var window = Open();

        Say(window, "Ada");

        Editor(window).History.Patch.Author.ShouldBe("Ada");
        Credit(window).ShouldBe("Ada");
        All<TextBlock>(window).ShouldContain(t => t.Text == "A");
        All<TextBlock>(window).ShouldContain(t => t.Text == "Made by");
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
        Press(All<Button>(window).Single(b => b.Name == "apply"));
        Settle(window);

        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = false;
        Editor(window).Selection.Select(null);
        Settle(window);

        Say(window, "Ada");
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
        Press(All<Button>(window).Single(b => b.Name == "remove-tag-slow"));
        Settle(window);

        Chips(window).ShouldBe(["drone", "ambient"]);
        Editor(window).History.Patch.Tags.ShouldBe(["drone", "ambient"]);
    }

    [AvaloniaFact]
    public void Taking_the_last_tag_away_leaves_the_patch_untagged_and_undo_puts_it_back()
    {
        var window = Open();

        TypeTags(window, "drone");
        Press(All<Button>(window).Single(b => b.Name == "remove-tag-drone"));
        Settle(window);

        Editor(window).History.Patch.Tags.ShouldBeNull();

        Editor(window).History.Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).History.Patch.Tags.ShouldBe(["drone"]);
    }

    [AvaloniaFact]
    public void The_box_for_a_tag_stands_where_the_chip_stood_at_its_size_and_in_its_color()
    {
        var window = Open();

        TypeTags(window, "drone");

        var slot = (Control)All<Button>(window).Single(b => b.Name == "patch-tag-add").Parent!;
        var before = slot.TranslatePoint(new Point(0, 0), window)!.Value;
        var size = slot.Bounds.Size;
        var drone = All<Border>(window).First(b => b.Child is StackPanel);

        var box = NewTagBox(window);
        var after = box.TranslatePoint(new Point(0, 0), window)!.Value;

        after.ShouldBe(before);
        box.Bounds.Height.ShouldBe(size.Height);
        box.Bounds.Width.ShouldBeGreaterThanOrEqualTo(size.Width);
        ((Avalonia.Media.ISolidColorBrush)box.Background!).Color.ShouldBe(((Avalonia.Media.ISolidColorBrush)drone.Background!).Color);
    }

    [AvaloniaFact]
    public void The_pencil_changes_who_made_it()
    {
        var window = Open();

        Say(window, "Ada");
        Click(window, "patch-author-edit");

        AuthorBox(window).Text.ShouldBe("Ada");
        AuthorBox(window).Text = "Grace";
        AuthorBox(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);

        Editor(window).History.Patch.Author.ShouldBe("Grace");
        Credit(window).ShouldBe("Grace");
    }

    [AvaloniaFact]
    public void The_cross_takes_the_credit_away_and_undo_puts_it_back()
    {
        var window = Open();

        Say(window, "Ada");
        Click(window, "patch-author-remove");

        Editor(window).History.Patch.Author.ShouldBeNull();
        All<Button>(window).ShouldContain(b => b.Name == "patch-author-add");

        Editor(window).History.Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).History.Patch.Author.ShouldBe("Ada");
    }

    [AvaloniaFact]
    public void Emptying_the_box_or_pressing_Escape_leaves_who_made_it_as_it_was()
    {
        var window = Open();

        Say(window, "Ada");

        Click(window, "patch-author-edit");
        AuthorBox(window).Text = "";
        AuthorBox(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);
        Editor(window).History.Patch.Author.ShouldBe("Ada");

        Click(window, "patch-author-edit");
        AuthorBox(window).Text = "Grace";
        AuthorBox(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Settle(window);
        Editor(window).History.Patch.Author.ShouldBe("Ada");
        Credit(window).ShouldBe("Ada");
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
