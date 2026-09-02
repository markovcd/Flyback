using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The patch as text beside the patch as a graph, and which of the two is the
/// document.
/// </summary>
/// <remarks>
/// The rule ADR-0068 settles on is that the file decides, not the view: a patch
/// opened as a graph is a graph, and the text view of it is a printing — a
/// reading rather than a round trip, because printing drops the groups and lays
/// the canvas out afresh. Applying a printing is how somebody deliberately takes
/// a patch into text, and from then on the text is the document and the canvas
/// is a view of it.
/// <para>
/// The window opens on a preset, which is a graph nobody wrote any text for. So
/// everything here starts from the canvas owning the patch.
/// </para>
/// </remarks>
public class SourceViewTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static ToggleButton CodeButton(MainWindow window) =>
        All<ToggleButton>(window).Single(b => b.Name == "code");

    private static TextEditor Text(MainWindow window) =>
        All<TextEditor>(window).Single(b => b.Name == "source");

    private static Button Apply(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "apply");

    private static StackPanel Inspector(MainWindow window) =>
        All<StackPanel>(window).Single(p => p.Name == "inspector");

    /// <summary>Shows the text view and lets the layout catch up.</summary>
    private static TextEditor ShowCode(MainWindow window)
    {
        CodeButton(window).IsChecked = true;
        Settle(window);

        return Text(window);
    }

    /// <summary>Puts text in and asks for it, as Ctrl+Enter and the button both do.</summary>
    private static void Evaluate(MainWindow window, string source)
    {
        ShowCode(window).Text = source;

        Apply(window).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    /// <summary>Presses Enter at the text box, with whatever is being held.</summary>
    private static void Press(TextEditor text, Avalonia.Input.KeyModifiers held) =>
        text.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
            KeyModifiers = held,
        });

    /// <summary>A tone: the clock, an oscillator and the Output it reaches.</summary>
    private const string Hum = """
        # one steady tone
        let hum = t |> sine(freq: 220)
        hum |> out.left
        """;

    // --- showing it ---------------------------------------------------------

    [AvaloniaFact]
    public void The_text_and_the_canvas_are_never_both_showing()
    {
        var window = Open();

        Editor(window).IsVisible.ShouldBeTrue();

        ShowCode(window);

        Editor(window).IsVisible.ShouldBeFalse();
        Text(window).IsVisible.ShouldBeTrue();

        CodeButton(window).IsChecked = false;
        Settle(window);

        Editor(window).IsVisible.ShouldBeTrue();
    }

    /// <summary>
    /// A patch that arrived as a graph stays one. The text is a printing of it,
    /// and says so above itself rather than letting somebody find out by losing
    /// their groups.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_is_read_as_text_without_becoming_text()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text.ShouldNotBeNullOrWhiteSpace();

        // Still the graph's patch: nothing is locked, and the inspector still
        // turns knobs.
        Editor(window).Locked.ShouldBeFalse();
        Inspector(window).IsEnabled.ShouldBeTrue();

        Notice(window).ShouldNotBeNull().ShouldContain("still the document");
    }

    /// <summary>What the printing says for itself, or null when there is nothing shown.</summary>
    private static string? Notice(MainWindow window) => All<TextBlock>(window)
        .Where(block => block.IsVisible && block.Text is { } said && said.Contains("Printed from"))
        .Select(block => block.Text)
        .FirstOrDefault();

    /// <summary>
    /// Typing is not thrown away by a second look at the canvas. Somebody who
    /// switched over to check a wire and came back would otherwise find their
    /// work replaced by a printing of a patch they had not changed.
    /// </summary>
    [AvaloniaFact]
    public void Switching_away_and_back_keeps_what_was_typed()
    {
        var window = Open();

        ShowCode(window).Text = Hum;

        CodeButton(window).IsChecked = false;
        Settle(window);

        ShowCode(window).Text.ShouldBe(Hum);
    }

    /// <summary>
    /// The editor is a code editor rather than a box with text in it: a gutter
    /// to say which line a complaint is about, and the language coloured so a
    /// module reads differently from the socket it is being handed. Neither can
    /// be had from a TextBox, which is why this costs a package.
    /// </summary>
    [AvaloniaFact]
    public void The_text_is_shown_as_code()
    {
        var window = Open();
        var text = ShowCode(window);

        text.ShowLineNumbers.ShouldBeTrue();

        text.SyntaxHighlighting.ShouldNotBeNull("the language's own definition should have loaded")
            .Name.ShouldBe("Flyback");
    }

    /// <summary>
    /// And the definition covers what the language actually has. Written by hand
    /// against docs/language.md, so this is what stops it drifting from the
    /// eight statement forms it is colouring.
    /// </summary>
    [AvaloniaFact]
    public void The_language_definition_names_the_words_the_language_has()
    {
        var window = Open();
        var colours = ShowCode(window).SyntaxHighlighting.ShouldNotBeNull();

        var named = colours.NamedHighlightingColors.Select(colour => colour.Name).ToList();

        named.ShouldContain("Comment");
        named.ShouldContain("Keyword");
        named.ShouldContain("Pipe");
        named.ShouldContain("Sink");
        named.ShouldContain("Socket");
    }

    // --- applying it --------------------------------------------------------

    [AvaloniaFact]
    public void Applying_the_text_puts_the_patch_it_describes_on_the_canvas()
    {
        var window = Open();

        Evaluate(window, Hum);

        var patch = Editor(window).Patch;

        // The clock, the oscillator and the Output — and nothing of whatever
        // preset the window opened on.
        patch.Nodes.Count.ShouldBe(3);
        patch.Nodes.ShouldContain(node => node.TypeId == "osc.sine");
    }

    /// <summary>
    /// And the gesture every live coding environment uses for it. Enter belongs
    /// to the text box, so the one that applies has to be one the box does not
    /// want.
    /// </summary>
    [AvaloniaFact]
    public void Control_enter_applies_the_text()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text = Hum;

        // Raised at the box rather than typed at the window: headless routing
        // needs an activated top level to decide where a keystroke lands, and
        // what is being checked here is the box's own handler — that Control
        // tells this Enter from the one that adds a line.
        Press(text, Avalonia.Input.KeyModifiers.Control);
        Settle(window);

        Editor(window).Patch.Nodes.Count.ShouldBe(3);

        // And a bare Enter is still the box's own, so typing a patch out over
        // several lines does not apply it four times on the way.
        var before = Editor(window).Patch;

        Press(text, Avalonia.Input.KeyModifiers.None);
        Settle(window);

        Editor(window).Patch.ShouldBeSameAs(before);
    }

    /// <summary>
    /// Applying a printing is how a patch is taken into text, and it is the one
    /// thing that changes who owns it. Said out loud rather than done quietly,
    /// because it changes what saving writes.
    /// </summary>
    [AvaloniaFact]
    public void Applying_makes_the_text_the_document()
    {
        var window = Open();

        Evaluate(window, Hum);

        Editor(window).Locked.ShouldBeTrue();
        Inspector(window).IsEnabled.ShouldBeFalse();
        Notice(window).ShouldBeNull("the text is the document now, so there is nothing to warn about");
    }

    /// <summary>
    /// A text that does not read costs nothing. The language builds a patch or
    /// refuses to, so there is no half-applied state to be left in — which is
    /// what makes an evaluation safe to try rather than something to be sure
    /// about first.
    /// </summary>
    [AvaloniaFact]
    public void A_text_that_does_not_read_leaves_the_patch_alone()
    {
        var window = Open();
        var before = Editor(window).Patch.Nodes.Count;

        Evaluate(window, "t |> sine(freq: 220) |> out.leftt");

        Editor(window).Patch.Nodes.Count.ShouldBe(before);
        Editor(window).Locked.ShouldBeFalse("nothing was applied, so nothing changed hands");
    }

    /// <summary>And says where the mistake is, in the words the language uses.</summary>
    [AvaloniaFact]
    public void A_text_that_does_not_read_says_which_line()
    {
        var window = Open();

        Evaluate(window, "t |> sine(freq: 220) |> out.leftt");

        All<TextBlock>(window)
            .Select(block => block.Text ?? string.Empty)
            .ShouldContain(said => said.StartsWith("2:") || said.StartsWith("1:"));
    }

    // --- taking it back, and laying it out ----------------------------------

    private static Button Tidy(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "tidy");

    private static Button Undo(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "undo");

    private static Button Redo(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "redo");

    private static void Press(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// The layout button lays out what is showing. On the canvas that is the
    /// modules; on the text it is the lines, which is the same thing done to the
    /// other view of one patch.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_folds_the_lines_while_the_text_is_showing()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text = "x |> sine(freq: 1.5) |> add(b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(saturation: 0.85) |> gain(gain: 0.5) |> out.color";

        Press(Tidy(window));
        Settle(window);

        text.Text.ShouldContain("\n  |> ");
        text.Text.ReplaceLineEndings("\n").Split('\n')
            .ShouldAllBe(line => line.Length <= Flyback.Core.Language.SourceLayout.Width);
    }

    /// <summary>
    /// And the key does what the button does. Ctrl+L is the window's, reached
    /// once the editor has decided it does not want the keystroke itself.
    /// </summary>
    [AvaloniaFact]
    public void Control_l_folds_the_lines_too()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text = "x |> sine(freq: 1.5) |> add(b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(saturation: 0.85) |> gain(gain: 0.5) |> out.color";

        window.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.L,
            KeyModifiers = Avalonia.Input.KeyModifiers.Control,
        });

        Settle(window);

        text.Text.ShouldContain("\n  |> ");
    }

    /// <summary>And it is one thing done, so one press of undo puts it back.</summary>
    [AvaloniaFact]
    public void Folding_the_lines_is_one_thing_to_take_back()
    {
        var window = Open();
        var text = ShowCode(window);

        const string long_ = "x |> sine(freq: 1.5) |> add(b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(saturation: 0.85) |> gain(gain: 0.5) |> out.color";

        text.Text = long_;

        Press(Tidy(window));
        Settle(window);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(long_);
    }

    /// <summary>
    /// Undo and redo follow the view too. Typing is taken back by the text's own
    /// stack, and the canvas's run of evaluations is not disturbed by it.
    /// </summary>
    [AvaloniaFact]
    public void Undo_takes_back_typing_while_the_text_is_showing()
    {
        var window = Open();
        var text = ShowCode(window);

        var printed = text.Text;

        // Through the document, which is what typing is. Assigning Text loads a
        // document instead, and loading one empties the stack on purpose.
        text.Document.Insert(0, "# a note to myself\n");
        Settle(window);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(printed);

        Press(Redo(window));
        Settle(window);

        text.Text.ShouldStartWith("# a note to myself");
    }

    /// <summary>
    /// And undo stops at the document it was given. A person who opened a source
    /// file, or took a printing of the canvas, should not be able to press Ctrl+Z
    /// back into the text of something else they had open earlier.
    /// </summary>
    [AvaloniaFact]
    public void Undo_does_not_reach_back_past_the_document_that_was_opened()
    {
        var window = Open();

        ShowCode(window);

        Undo(window).IsEnabled.ShouldBeFalse("nothing has been typed into this printing yet");
    }

    /// <summary>
    /// And an evaluation is still on the canvas's stack after all that typing,
    /// which is what makes switching back to it worth doing.
    /// </summary>
    [AvaloniaFact]
    public void The_canvas_keeps_its_own_history_through_an_afternoon_of_typing()
    {
        var window = Open();

        Evaluate(window, Hum);

        var applied = Editor(window).Patch.Nodes.Count;

        ShowCode(window).Text = Hum + "\n# and some more typing";
        Settle(window);

        CodeButton(window).IsChecked = false;
        Settle(window);

        Editor(window).Patch.Nodes.Count.ShouldBe(applied);

        Press(Undo(window));
        Settle(window);

        // Back to the preset the window opened on, which the evaluation replaced.
        Editor(window).Patch.Nodes.Count.ShouldNotBe(applied);
    }

    /// <summary>
    /// The button is off for the one case where it would do nothing that lasts:
    /// a canvas built from text is laid out again on the next apply.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_a_locked_canvas_is_not_offered()
    {
        var window = Open();

        Tidy(window).IsEnabled.ShouldBeTrue();

        Evaluate(window, Hum);

        Tidy(window).IsEnabled.ShouldBeTrue("the text is showing, so it folds the lines");

        CodeButton(window).IsChecked = false;
        Settle(window);

        Tidy(window).IsEnabled.ShouldBeFalse("the canvas is a view, and a layout would not survive");
    }

    // --- the canvas as a view -----------------------------------------------

    /// <summary>
    /// A locked canvas keeps everything that looks and loses everything that
    /// changes. Deleting is the sharpest of those: the module would come
    /// straight back on the next evaluation, so the key is off rather than
    /// undone a moment later.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_will_not_delete_a_module()
    {
        var window = Open();

        Evaluate(window, Hum);

        var editor = Editor(window);

        editor.SelectAll();
        editor.Focus();

        var before = editor.Patch.Nodes.Count;

        window.KeyPress(
            Avalonia.Input.Key.Delete,
            Avalonia.Input.RawInputModifiers.None,
            Avalonia.Input.PhysicalKey.Delete,
            null);
        Settle(window);

        editor.Patch.Nodes.Count.ShouldBe(before);
    }

    /// <summary>
    /// And the panel that lists what the canvas can do lists what it can
    /// actually do. Naming gestures that are switched off would have somebody
    /// following them and concluding the program was broken.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_does_not_offer_gestures_it_has_taken_away()
    {
        var window = Open();

        Evaluate(window, Hum);

        var said = string.Join(
            "\n",
            All<TextBlock>(window).Select(block => block.Text ?? string.Empty));

        said.ShouldNotContain("Right-click the canvas");
        said.ShouldNotContain("Delete removes what is selected");
        said.ShouldContain("Press F2 to go back to it");
    }

    /// <summary>
    /// But it still selects, because that is how somebody reads a patch and
    /// picks the module the inspector should be about.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_still_selects()
    {
        var window = Open();

        Evaluate(window, Hum);

        var editor = Editor(window);

        editor.SelectAll();
        editor.SelectedNodes.Count.ShouldBe(editor.Patch.Nodes.Count);
    }
}
