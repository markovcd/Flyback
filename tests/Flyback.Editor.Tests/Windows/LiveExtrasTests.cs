using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Hubs;
using Flyback.Editor.Inspect;
using Flyback.Editor.Tests.Controls;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// What a selected module carries, over the live picture: the transport's [ ] button shows
/// while the module carries any and its rows are drawn, and it or Ctrl+I opens the card.
/// </summary>
public class LiveExtrasTests : EditorTest
{
    private static Patch Snapped(out NodeInstance quantiser, out NodeInstance sine)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        sine = builder.Add("osc.sine", 0, 0);
        quantiser = builder.Add(PitchModules.QuantiserTypeId, 300, 0);
        var sink = builder.Add(OutputModule.TypeId, 600, 0);

        builder.Wire(sine, 0, quantiser, 0).Wire(quantiser, 0, sink, OutputModule.LeftPort);

        return builder.Patch;
    }

    private static void Press(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    private static void GoLive(MainWindow window) => Press(window, Key.F11, RawInputModifiers.Shift, PhysicalKey.F11);

    private static void Click(MainWindow window, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    private static Button ExtrasButton(MainWindow window) => All<Button>(window).Single(b => b.Name == "liveExtras");

    private static void Select(MainWindow window, NodeInstance node)
    {
        Editor(window).Selection.Select(node.Id);
        Editor(window).Selection.Announce();
        Settle(window);
    }

    [AvaloniaFact]
    public void The_extras_button_shows_only_for_a_selected_module_that_carries_extras()
    {
        var window = Open(Snapped(out var quantiser, out var sine));

        GoLive(window);
        ExtrasButton(window).IsVisible.ShouldBeFalse("nothing is selected");

        Select(window, sine);
        ExtrasButton(window).IsVisible.ShouldBeFalse("a sine has knobs and nothing else");

        Select(window, quantiser);
        ExtrasButton(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_extras_button_opens_the_card_with_the_inspector_s_own_controls()
    {
        var window = Open(Snapped(out var quantiser, out _));
        var extras = Service<LiveExtras>(window);

        GoLive(window);
        Select(window, quantiser);
        Click(window, ExtrasButton(window));

        extras.IsOpen.ShouldBeTrue();
        extras.Card.IsEffectivelyVisible.ShouldBeTrue("over the picture");
        All<Button>(extras.Card).Count(b => b.Classes.Contains(ScaleKeys.KeyTag)).ShouldBe(Pitch.Classes, "the scale's octave");

        Click(window, ExtrasButton(window));

        extras.IsOpen.ShouldBeFalse("the same button shuts it");
    }

    [AvaloniaFact]
    public void The_card_hangs_under_the_module_it_edits_and_names_it()
    {
        var window = Open(Snapped(out var quantiser, out _));
        var extras = Service<LiveExtras>(window);
        var editor = Editor(window);

        GoLive(window);
        Select(window, quantiser);
        Click(window, ExtrasButton(window));

        var box = editor.View.OnScreen(editor.Geometry.Bounds(quantiser, NodeCatalog.Get(quantiser.TypeId)!));
        var card = extras.Card.TranslatePoint(default, editor)!.Value;

        All<TextBlock>(extras.Card).First().Text.ShouldBe("Quantiser · scale");
        card.Y.ShouldBeGreaterThan(box.Bottom, "under the module");
        (card.X + extras.Card.Bounds.Width / 2).ShouldBe(box.Center.X, 1, "centered on it");
    }

    [AvaloniaFact]
    public void A_short_choice_on_the_card_is_a_row_of_buttons_and_a_tap_keeps_it()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var ink = builder.Add("color.ink", 300, 0);
        builder.Wire(ink, 0, builder.Add(OutputModule.TypeId, 600, 0), OutputModule.ColorPort);

        var window = Open(builder.Patch);
        var extras = Service<LiveExtras>(window);
        var placed = Editor(window).History.Patch.Find(ink.Id)!;

        GoLive(window);
        Select(window, placed);
        Click(window, ExtrasButton(window));

        var chips = All<Button>(extras.Card).Where(b => b.Classes.Contains(ChoiceChips.ChipTag)).ToList();
        chips.ShouldNotBeEmpty("a list this short is buttons");
        All<Picker>(extras.Card).ShouldBeEmpty();

        Click(window, chips.Single(b => b.Content is "Over"));

        (placed.StateOf(ColorModules.InkStateKey)?[ColorModules.InkModeKey]?.GetValue<string>() ?? "").ShouldBe(ColorModules.InkOver);
    }

    [AvaloniaFact]
    public void Opening_the_card_again_puts_it_where_it_was()
    {
        var window = Open(Snapped(out var quantiser, out _));
        var extras = Service<LiveExtras>(window);

        GoLive(window);
        Select(window, quantiser);

        var places = new List<Thickness>();

        for (var i = 0; i < 4; i++)
        {
            Press(window, Key.I, RawInputModifiers.Control, PhysicalKey.I);
            places.Add(extras.Card.Margin);
            Press(window, Key.I, RawInputModifiers.Control, PhysicalKey.I);
        }

        places.Distinct().Count().ShouldBe(1, string.Join(" | ", places));
    }

    [AvaloniaFact]
    public void A_key_on_the_card_changes_the_scale_and_the_text_that_owns_it()
    {
        var window = Open();
        var extras = Service<LiveExtras>(window);

        SourceViewTest.Evaluate(window, "sine() |> quantiser() [ C D E G A ] |> out.left");
        SourceViewTest.Click(window, "quantiser");
        GoLive(window);
        Press(window, Key.I, RawInputModifiers.Control, PhysicalKey.I);

        extras.IsOpen.ShouldBeTrue("Ctrl+I opens the card for the call the caret is in");

        var sharp = All<Button>(extras.Card).Single(b => b.Classes.Contains(ScaleKeys.KeyTag) && b.Content is TextBlock { Text: "C#" });
        Click(window, sharp);
        Service<TextWriteBack>(window).HandCameOff();
        Settle(window);

        SourceViewTest.Text(window).Text.Trim().ShouldBe("sine() |> quantiser() [ C C# D E G A ] |> out.left");
    }

    [AvaloniaFact]
    public void Zoomed_out_past_reading_there_are_no_extras_to_open()
    {
        var window = Open(Snapped(out var quantiser, out _));
        var extras = Service<LiveExtras>(window);
        var editor = Editor(window);

        GoLive(window);
        Select(window, quantiser);
        Click(window, ExtrasButton(window));

        while (!editor.View.Overview) editor.View.ZoomBy(new Point(0, 0), 0.8);
        Settle(window);

        extras.IsOpen.ShouldBeFalse("the card shuts as the rows go");
        ExtrasButton(window).IsVisible.ShouldBeFalse();

        Press(window, Key.I, RawInputModifiers.Control, PhysicalKey.I);
        extras.IsOpen.ShouldBeFalse("and Ctrl+I opens nothing");
    }

    [AvaloniaFact]
    public void Escape_shuts_the_card_before_it_leaves_the_picture()
    {
        var window = Open(Snapped(out var quantiser, out _));
        var extras = Service<LiveExtras>(window);

        GoLive(window);
        Select(window, quantiser);
        Press(window, Key.I, RawInputModifiers.Control, PhysicalKey.I);
        Press(window, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);

        extras.IsOpen.ShouldBeFalse();
        Service<LivePatching>(window).IsOn.ShouldBeTrue("the first Escape is the card's");
    }
}
