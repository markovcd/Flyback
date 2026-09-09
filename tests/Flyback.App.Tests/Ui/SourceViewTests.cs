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
        Notice(window).ShouldBeNull("the text is the document now, so there is nothing to warn about");

        Inspector(window).IsEnabled
            .ShouldBeTrue("a knob turned on a locked canvas is written back into the text");
    }

    private static Button Hand(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "hand");

    /// <summary>Pumps until the question about the text has arrived.</summary>
    private static ModalOverlay Asking(MainWindow window)
    {
        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        return All<ModalOverlay>(window).SingleOrDefault()
            ?? throw new InvalidOperationException("emptying unsaved text should have asked first");
    }

    /// <summary>Hands the patch back, answering the question that fronts it.</summary>
    private static void HandBack(MainWindow window, string answer = "Discard changes")
    {
        Press(Hand(window));
        Press(All<Button>(Asking(window)).Single(b => b.Content as string == answer));

        Settle(window);
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    /// <summary>
    /// And the other way, which is what applying is in reverse. Without it the
    /// adoption is a one-way door: the canvas stays a view until some other
    /// document arrives, so drawing a single wire means saving the patch as a
    /// <c>.fbk</c> first.
    /// </summary>
    [AvaloniaFact]
    public void Handing_it_back_makes_the_canvas_the_document_again()
    {
        var window = Open();

        Evaluate(window, Hum);

        var applied = Editor(window).Patch.Nodes.Count;

        HandBack(window);

        Editor(window).Locked.ShouldBeFalse();
        Editor(window).IsVisible.ShouldBeTrue("the canvas is what somebody was trying to get to");
        Editor(window).Patch.Nodes.Count.ShouldBe(applied, "nothing was rebuilt to hand it over");

        // And the text is a reading of that patch again, printed afresh over a
        // buffer the handover emptied.
        ShowCode(window).Text.ShouldNotBeNullOrWhiteSpace();
        Notice(window).ShouldNotBeNull().ShouldContain("still the document");
    }

    private static ComboBox Presets(MainWindow window) =>
        All<ComboBox>(window).Single(box => box.Name == "presets");

    /// <summary>
    /// A preset picked from the text view is read into text. Which view somebody
    /// picks one from says which of the two they mean to work in, and leaving
    /// them in the text over a canvas they can still drag modules around on is
    /// the question this design exists to settle, put back on the screen.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_picked_while_the_text_is_up_is_read_into_text()
    {
        var window = Open();

        var before = ShowCode(window).Text;

        Presets(window).SelectedIndex = 1;
        Settle(window);

        Text(window).IsVisible.ShouldBeTrue("the text is the view the preset was picked from");
        Editor(window).IsVisible.ShouldBeFalse();

        // What it shows is the patch that has just arrived, and that text is now
        // the document rather than a printing offered for reading.
        Text(window).Text.ShouldNotBeNullOrWhiteSpace();
        Text(window).Text.ShouldNotBe(before);

        Editor(window).Locked.ShouldBeTrue("the text is the document, so the canvas is a view");
        Notice(window).ShouldBeNull("a printing that has been taken is not still offered");
    }

    /// <summary>
    /// And it arrives with nothing to lose, exactly as one picked on the canvas
    /// does. The reading is made by this program out of a patch nobody has
    /// touched, so a title claiming unsaved work would be claiming somebody
    /// else's.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_read_into_text_has_nothing_to_lose_yet()
    {
        var window = Open();

        ShowCode(window);

        Presets(window).SelectedIndex = 1;
        Settle(window);

        Editor(window).IsModified.ShouldBeFalse();
        window.Title.ShouldNotBeNull().ShouldNotContain("•");
    }

    /// <summary>
    /// And with nothing to take back either. The evaluation that read it in is
    /// how the preset arrived, not something somebody did to it, so undo has to
    /// stop here — one that walked past it would take the arrival back and put
    /// the canvas up holding the patch from before the preset was picked, which
    /// is not a step anybody asked for.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_read_into_text_has_nothing_to_take_back()
    {
        var window = Open();

        ShowCode(window);

        Presets(window).SelectedIndex = 1;
        Settle(window);

        Undo(window).IsEnabled.ShouldBeFalse("nothing has been done to this patch yet");

        var read = Text(window).Text;
        var modules = Editor(window).Patch.Nodes.Count;

        // Pressed anyway, since the two stacks are what answer for the gesture
        // and a button that only looks off would still be answered by them.
        Press(Undo(window));
        Settle(window);

        Text(window).IsVisible.ShouldBeTrue("undo has nowhere to go, so the view does not move");
        Text(window).Text.ShouldBe(read);
        Editor(window).Patch.Nodes.Count.ShouldBe(modules);
        Editor(window).Locked.ShouldBeTrue("the text is still the document");
    }

    /// <summary>
    /// A preset picked from the canvas is still the graph's, which is the case
    /// ADR-0068 settled and this does not disturb.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_picked_from_the_canvas_is_still_the_graphs()
    {
        var window = Open();

        Presets(window).SelectedIndex = 1;
        Settle(window);

        Editor(window).Locked.ShouldBeFalse();
        Notice(window).ShouldBeNull("nothing has printed it — the text view has not been opened");

        ShowCode(window);

        Notice(window).ShouldNotBeNull().ShouldContain("still the document");
    }

    /// <summary>
    /// Handing the patch back is the one gesture that does move the view, since
    /// wanting to draw on the canvas is the whole of what it means.
    /// </summary>
    [AvaloniaFact]
    public void Handing_it_back_is_what_moves_the_view()
    {
        var window = Open();

        Evaluate(window, Hum);

        Text(window).IsVisible.ShouldBeTrue();

        HandBack(window);

        Editor(window).IsVisible.ShouldBeTrue();
        CodeButton(window).IsChecked.ShouldBe(false, "the toggle says which view is showing");
    }

    /// <summary>
    /// Offered only where it would change something: over a printing the canvas
    /// has the patch already, and a button saying so would do nothing.
    /// </summary>
    [AvaloniaFact]
    public void Handing_back_is_offered_only_while_the_text_is_the_document()
    {
        var window = Open();

        ShowCode(window);

        Hand(window).IsVisible.ShouldBeFalse("this is a printing — the canvas owns the patch");

        Evaluate(window, Hum);

        Hand(window).IsVisible.ShouldBeTrue();

        HandBack(window);
        ShowCode(window);

        Hand(window).IsVisible.ShouldBeFalse();
    }

    /// <summary>
    /// The handover empties the buffer and writes it nowhere on the way, so
    /// typing nobody has saved is asked about first — and a refusal leaves both
    /// the text and who owns it exactly as they were.
    /// </summary>
    [AvaloniaFact]
    public void Handing_back_asks_before_it_empties_unsaved_text()
    {
        var window = Open();

        Evaluate(window, Hum);

        Press(Hand(window));

        var dialog = Asking(window);

        All<TextBlock>(dialog)
            .Select(block => block.Text ?? string.Empty)
            .ShouldContain("Unsaved text");

        Press(All<Button>(dialog).Single(b => b.Content as string == "Cancel"));
        Settle(window);

        Editor(window).Locked.ShouldBeTrue("the question was refused, so nothing changed hands");
        Text(window).Text.ShouldBe(Hum);
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

    /// <summary>Types a run of characters, one keystroke at a time.</summary>
    /// <remarks>
    /// Through the keyboard rather than into the document, because grouping a
    /// run of typing is something done to keystrokes: text put straight into the
    /// document is not typing and is one edit already.
    /// </remarks>
    private static void Type(MainWindow window, string said)
    {
        foreach (var letter in said)
        {
            window.KeyTextInput(letter.ToString());
            Dispatcher.UIThread.RunJobs();
        }

        Settle(window);
    }

    /// <summary>
    /// A run of typing comes back a word at a time, the way it does in every
    /// other editor.
    /// </summary>
    /// <remarks>
    /// The stack underneath takes an operation per change to the document, and a
    /// change is a keystroke — so a sentence used to come back one letter at a
    /// time, which is nobody's idea of Ctrl+Z. The run is grouped as it is typed
    /// and the space that ends a word belongs to the word, so what a press
    /// leaves is the line as it stood before that word rather than the word with
    /// its space still after it.
    /// </remarks>
    [AvaloniaFact]
    public void A_run_of_typing_comes_back_a_word_at_a_time()
    {
        var window = Open();
        var text = ShowCode(window);

        var printing = text.Text;

        text.TextArea.Focus();
        text.CaretOffset = 0;
        Settle(window);

        Type(window, "hello world");

        text.Text.ShouldStartWith("hello world");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldStartWith("hello ", customMessage: "the last word is what one press takes back");
        text.Text.ShouldNotStartWith("hello w");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(printing, "and the first word after it");
    }

    /// <summary>
    /// A word typed after something that is not typing is a step of its own.
    /// </summary>
    /// <remarks>
    /// The run is a run of keystrokes and nothing else. Anything else that moves
    /// the text — a value written back from the panel, a document loaded, an
    /// undo — ends it, or a word would fold into an edit nobody typed and one
    /// press would take back both.
    /// </remarks>
    [AvaloniaFact]
    public void Typing_does_not_join_what_was_not_typed()
    {
        var window = Open();
        var text = ShowCode(window);

        text.TextArea.Focus();
        text.CaretOffset = 0;
        Settle(window);

        // Not typing: put in as one edit, the way the panel writes a knob back.
        text.Document.Insert(0, "# ");
        Settle(window);

        Type(window, "note");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldStartWith("# ", customMessage: "the word came back on its own");
        text.Text.ShouldNotStartWith("# n");
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
    /// And an evaluation is a handover as much as an edit, so taking it back
    /// takes the handover back with it.
    /// </summary>
    /// <remarks>
    /// Applying is how a patch is taken into text. Undo it and what is on the
    /// canvas is a patch no text describes — laid out where it stood before the
    /// build re-placed everything — so leaving the text as the document would
    /// lock that canvas behind a printing of something else, with a handover
    /// made by hand as the only way out. That is not what Ctrl+Z was pressed
    /// for: it was pressed to be back where the modules were, which is why the
    /// view goes back too.
    /// </remarks>
    [AvaloniaFact]
    public void Undoing_an_evaluation_gives_the_patch_back_to_the_canvas()
    {
        var window = Open();

        Evaluate(window, Hum);

        Editor(window).Locked.ShouldBeTrue("applying is how the text becomes the document");

        // Pressed at the text view, which is where applying leaves somebody.
        // Nothing was typed to make this printing, so the text has nothing of
        // its own to take back and the gesture is the patch's.
        Press(Undo(window));
        Settle(window);

        Editor(window).Locked.ShouldBeFalse("the evaluation that took it into text went back too");
        Editor(window).IsVisible.ShouldBeTrue("and the view went back with it");
        Notice(window).ShouldNotBeNull().ShouldContain("still the document");

        Press(Redo(window));
        Settle(window);

        Editor(window).Locked.ShouldBeTrue("and putting the evaluation back takes it into text");
        Editor(window).IsVisible.ShouldBeFalse("which is something done at the text view");
    }

    /// <summary>
    /// And the canvas reaches it too, for somebody who switched over to look at
    /// what the evaluation did to their modules before deciding against it.
    /// </summary>
    [AvaloniaFact]
    public void Undoing_an_evaluation_from_the_canvas_gives_it_back_as_well()
    {
        var window = Open();

        Evaluate(window, Hum);

        CodeButton(window).IsChecked = false;
        Settle(window);

        Press(Undo(window));
        Settle(window);

        Editor(window).Locked.ShouldBeFalse();
        Editor(window).IsVisible.ShouldBeTrue("which is where they already were");
    }

    /// <summary>
    /// Typing is still the text's own, and still comes back first. The gesture
    /// falls through only where the view it is at has nothing left to take back.
    /// </summary>
    [AvaloniaFact]
    public void Typing_comes_back_before_the_handover_does()
    {
        var window = Open();

        Evaluate(window, Hum);

        var text = Text(window);

        text.Document.Insert(0, "# a note to myself\n");
        Settle(window);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(Hum);
        Editor(window).Locked.ShouldBeTrue("the typing was what there was to take back");

        Press(Undo(window));
        Settle(window);

        Editor(window).Locked.ShouldBeFalse("and now there is nothing but the handover");
    }

    /// <summary>
    /// A handover made by hand is not something an undo takes back. No edit was
    /// recorded to make it — nothing moved and no wire was drawn — so the steps
    /// behind it belong to the canvas that now holds the patch.
    /// </summary>
    [AvaloniaFact]
    public void Handing_it_back_is_not_undone_by_taking_an_edit_back()
    {
        var window = Open();

        Evaluate(window, Hum);
        HandBack(window);

        var editor = Editor(window);

        editor.Patch.Nodes[0].X += 40;
        editor.NotifyPatchChanged();
        Settle(window);

        Press(Undo(window));
        Settle(window);

        editor.Locked.ShouldBeFalse("the canvas was given the patch back and still has it");
    }

    /// <summary>
    /// A printing keeps up with an undo as it keeps up with a knob.
    /// </summary>
    /// <remarks>
    /// The knob is turned on the canvas before the text view has ever been
    /// opened, so nothing writes it into a printing on the way — the printing is
    /// made afterwards, from the patch as it then stands. Taking the turn back
    /// moves that patch out from under the reading, and a reading that went on
    /// saying the old number would be a reading of nothing.
    /// </remarks>
    [AvaloniaFact]
    public void A_printing_keeps_up_with_an_undo_on_the_canvas()
    {
        var window = Open();
        var editor = Editor(window);

        var reading = Flyback.Core.Language.PatchPrinter.Print(editor.Patch);

        // An edit on the canvas, before the text view has ever been opened, so
        // nothing writes it into a printing on the way.
        editor.Patch.Remove(editor.Patch.Nodes
            .First(node => node.TypeId != Flyback.Core.Graph.NodeCatalog.OutputTypeId).Id);

        editor.NotifyPatchChanged();
        Settle(window);

        var text = ShowCode(window);

        text.Text.ShouldNotBe(reading, "the printing is made from the patch as it now stands");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(reading, "and it goes back with the patch");
    }

    /// <summary>
    /// A knob turned over a printing is taken back on the canvas, where the only
    /// history of that patch is — and the reading is made afresh from it.
    /// </summary>
    /// <remarks>
    /// What the write-back put in the text is not an edit anybody made; it is
    /// the reading keeping up. Answering Ctrl+Z with it would put the old number
    /// back over a patch still playing the new one, and leave the text no longer
    /// the printing it says it is — which is what stops the caret pointing the
    /// panel, since a text that is not the printing maps to nothing.
    /// </remarks>
    [AvaloniaFact]
    public void Taking_back_a_knob_turned_over_a_printing_takes_back_the_knob()
    {
        var window = Open();
        var text = ShowCode(window);

        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        var chosen = Editor(window).SelectedNode.ShouldNotBeNull();
        var was = chosen.InputValues[0];
        var reading = text.Text;

        Turn(window, 0.375d);

        text.Text.ShouldContain("0.375");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(reading, "the reading is made afresh from the patch that came back");
        Editor(window).Patch.Find(chosen.Id).ShouldNotBeNull().InputValues[0].ShouldBe(was);

        // And it is the printing again, so the caret still points the panel.
        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        Editor(window).SelectedNode.ShouldNotBeNull().Id.ShouldBe(chosen.Id);
    }

    /// <summary>
    /// An undo that crosses no handover is not one, and says nothing about one.
    /// </summary>
    /// <remarks>
    /// An edit made on the canvas and taken back while a printing of that canvas
    /// happens to be showing changes nothing about who owns the patch — nobody
    /// switched anything. Announcing that the canvas is the document again would
    /// be telling somebody about a switch they never made, and throwing the
    /// printing away with it would drop them back on the canvas they were
    /// looking away from.
    /// </remarks>
    [AvaloniaFact]
    public void Taking_back_a_canvas_edit_under_a_printing_is_not_a_handover()
    {
        var window = Open();
        var editor = Editor(window);

        // On the canvas, which is where the window opens and where it still is.
        editor.Patch.Nodes[0].X += 40;
        editor.NotifyPatchChanged();
        Settle(window);

        var text = ShowCode(window);

        Press(Undo(window));
        Settle(window);

        editor.Locked.ShouldBeFalse("the canvas had the patch all along");
        editor.IsVisible.ShouldBeFalse("and nobody asked to be taken off the text");
        Notice(window).ShouldNotBeNull().ShouldContain("still the document");

        // Still the printing it was, so the caret still points the panel at the
        // module under it — which a handover would have thrown away.
        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        editor.SelectedNode.ShouldNotBeNull();
    }

    /// <summary>The one knob the patches used here have.</summary>
    private static float Knob(MainWindow window) =>
        Editor(window).Patch.Nodes.Single(node => node.TypeId == "math.atan2").InputValues[0];

    /// <summary>
    /// Applying is a thing done to the document, so it goes on the document's
    /// stack beside the typing that led to it — and being the last thing done,
    /// it is the first thing back.
    /// </summary>
    /// <remarks>
    /// The two used to be kept apart, the typing at the text and the evaluations
    /// on the canvas, and neither knew when the other had happened. So Ctrl+Z
    /// after an apply took back a line somebody had typed some minutes earlier
    /// and left the patch it had already been built into exactly where it was.
    /// </remarks>
    [AvaloniaFact]
    public void An_apply_comes_back_before_the_typing_that_led_to_it()
    {
        var window = Open();

        Evaluate(window, "atan2(a: 0.25) |> out.left");

        var text = Text(window);

        Knob(window).ShouldBe(0.25f);

        // Typed rather than loaded, which is what keeps it on the text's stack.
        text.Document.Replace(text.Text.IndexOf("0.25", StringComparison.Ordinal), 4, "0.75");
        Settle(window);

        Press(Apply(window));
        Settle(window);

        Knob(window).ShouldBe(0.75f);

        Press(Undo(window));
        Settle(window);

        Knob(window).ShouldBe(0.25f, "the evaluation was the last thing done");
        // And the typing is still there, to come back after it.
        text.Text.ShouldContain("0.75");

        Press(Undo(window));
        Settle(window);

        text.Text.Trim().ShouldBe("atan2(a: 0.25) |> out.left");
    }

    /// <summary>
    /// And a knob turned in the panel is one thing done as well, however little
    /// of it is text: the number in the code and the value behind it come back
    /// in one press.
    /// </summary>
    /// <remarks>
    /// Taking back only the text would leave the two saying different things
    /// about one patch — the file reading 1.5524476 over a patch that is still
    /// playing 2 — until the next apply, which would then quietly undo the knob
    /// as a side effect of building something else.
    /// </remarks>
    [AvaloniaFact]
    public void Undoing_a_knob_turned_in_the_panel_takes_the_value_back_too()
    {
        var window = Open();

        Evaluate(window, "atan2(a: 1.5524476) |> out.left");
        Click(window, "atan2");

        Turn(window, 2d);

        Text(window).Text.Trim().ShouldBe("atan2(a: 2) |> out.left");
        Knob(window).ShouldBe(2f);

        Press(Undo(window));
        Settle(window);

        Text(window).Text.Trim().ShouldBe("atan2(a: 1.5524476) |> out.left");
        Knob(window).ShouldBe(1.5524476f, "the number and what it does come back together");
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

    // --- the caret points the panel -----------------------------------------

    /// <summary>Puts the caret where a word is, as a click in the text would.</summary>
    private static void Click(MainWindow window, string word)
    {
        var text = Text(window);

        text.CaretOffset = text.Text.IndexOf(word, StringComparison.Ordinal) + 1;
        Settle(window);
    }

    /// <summary>
    /// The caret is the code view's pointer, and the panel follows it exactly as
    /// it follows a click on the canvas.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_points_the_panel()
    {
        var window = Open();

        Evaluate(window, Hum);
        Click(window, "sine");

        Editor(window).SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
    }

    /// <summary>
    /// A module written with no name of its own is still a module, and the whole
    /// point of pointing by position rather than by name is that it can be
    /// clicked without the text being rewritten to name it.
    /// </summary>
    [AvaloniaFact]
    public void A_module_with_no_name_is_pointed_at_too()
    {
        var window = Open();

        Evaluate(window, "atan2(a: 1.5524476) |> out.left");
        Click(window, "atan2");

        Editor(window).SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("math.atan2");
    }

    /// <summary>
    /// A printing is what the code view shows for a patch built on the canvas,
    /// and it has to be as clickable as text somebody wrote.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_points_the_panel_in_a_printing_too()
    {
        var window = Open();

        ShowCode(window);

        var text = Text(window);
        var call = text.Text.IndexOf('(');

        call.ShouldBeGreaterThan(0, "the preset prints as calls");

        text.CaretOffset = call;
        Settle(window);

        Editor(window).SelectedNode.ShouldNotBeNull();
    }

    // --- and a knob turned in it reaches the text ---------------------------

    /// <summary>Turns the first knob the panel is showing, and lets go of it.</summary>
    private static void Turn(MainWindow window, double to)
    {
        var slider = All<Slider>(window).First();

        slider.Value = to;
        Settle(window);

        Release(slider);
        Settle(window);
    }

    /// <summary>Lets go of the pointer over a control, which is what ends a gesture.</summary>
    private static void Release(Control over) =>
        over.RaiseEvent(new Avalonia.Input.PointerReleasedEventArgs(
            over,
            new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            over,
            default,
            0,
            default,
            Avalonia.Input.KeyModifiers.None,
            Avalonia.Input.MouseButton.Left)
        {
            RoutedEvent = Avalonia.Input.InputElement.PointerReleasedEvent,
        });

    /// <summary>
    /// The number is changed where the text already says it. Saying it a second
    /// time further down would leave the file asserting two different values for
    /// one socket, with the older one still written a few lines up.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_turned_in_the_panel_is_written_where_the_code_says_it()
    {
        var window = Open();

        Evaluate(window, "atan2(a: 1.5524476) |> out.left");
        Click(window, "atan2");

        Turn(window, 2d);

        Text(window).Text.Trim().ShouldBe("atan2(a: 2) |> out.left");
    }

    /// <summary>
    /// A drag is one gesture and one edit. Writing per frame would put a hundred
    /// things on the undo stack and flicker the line under whoever is reading it.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_still_being_dragged_has_not_reached_the_text_yet()
    {
        var window = Open();

        Evaluate(window, "atan2(a: 1.5524476) |> out.left");
        Click(window, "atan2");

        var slider = All<Slider>(window).First();

        slider.Value = 2d;
        Settle(window);

        Text(window).Text.ShouldContain("1.5524476");

        // And the engine has it all the same, which is the point of turning a
        // knob while a patch is playing.
        Editor(window).SelectedNode.ShouldNotBeNull().InputValues[0].ShouldBe(2f);
    }

    /// <summary>
    /// A printing keeps up with the knobs, in place. It is a reading of the
    /// canvas, and one that stopped agreeing with it the moment anybody turned
    /// something would be a reading of nothing.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_turned_over_a_printing_keeps_the_printing_true()
    {
        var window = Open();
        var text = ShowCode(window);

        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        var chosen = Editor(window).SelectedNode.ShouldNotBeNull();
        var before = text.Text;

        Turn(window, 0.375d);

        text.Text.ShouldNotBe(before, "the printing has to keep up with the knob");
        text.Text.ShouldContain("0.375");

        // Still the canvas's patch, and still pointed at: a printing written
        // into by this is a printing still.
        Editor(window).Locked.ShouldBeFalse();
        Notice(window).ShouldNotBeNull();

        text.CaretOffset = 0;
        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        Editor(window).SelectedNode.ShouldNotBeNull().Id.ShouldBe(chosen.Id);
    }

    /// <summary>
    /// Writing a knob back moves the caret, because the text under it just got
    /// shorter or longer. That is the text moving and not the caret, and the
    /// selection must not follow it.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_written_back_under_the_caret_leaves_the_panel_alone()
    {
        var window = Open();
        var text = ShowCode(window);

        // After the value that is about to change, so replacing it shifts the
        // caret and the view reports a move nobody made.
        text.CaretOffset = text.Text.IndexOf(')');
        Settle(window);

        var chosen = Editor(window).SelectedNode.ShouldNotBeNull();

        Turn(window, 0.375d);

        Editor(window).SelectedNode
            .ShouldNotBeNull("the panel emptied itself while the knob was being let go of")
            .Id.ShouldBe(chosen.Id);

        // And the caret still points the panel afterwards, rather than the map
        // being left empty for the rest of the session.
        var second = text.Text.IndexOf('(', text.Text.IndexOf(')'));

        second.ShouldBeGreaterThan(0, "the preset prints as more than one call");

        text.CaretOffset = second;
        Settle(window);

        Editor(window).SelectedNode.ShouldNotBeNull("the caret stopped pointing the panel");
    }

    /// <summary>
    /// A tune is not a knob and reaches the text the same way all the same. It
    /// goes back into the block the text already carries, which is the only
    /// place the language reads one.
    /// </summary>
    [AvaloniaFact]
    public void A_tune_edited_in_the_panel_is_written_where_the_block_stands()
    {
        var window = Open();

        Evaluate(window, "let riff = notes() [ A3 C4 ]\nriff |> out.left");
        Click(window, "notes");

        // The box holding the first step, found by what it holds: A3 is 57 and
        // no knob on a sequencer rests there.
        var step = All<NumericUpDown>(window).First(box => box.Value == 57m);

        step.Value = 60m;
        Settle(window);

        // The block waits for the hand to come off it, the same as a knob does.
        Text(window).Text.ShouldContain("[ A3 C4 ]");

        Release(All<Slider>(window).First());
        Settle(window);

        Text(window).Text.Trim().ShouldBe("let riff = notes() [ C4 C4 ]\nriff |> out.left");
    }

    /// <summary>
    /// A knob sitting at its default is written nowhere, so there is no number
    /// to change and it is added to the call that placed the module.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_the_code_does_not_mention_is_added_to_the_call()
    {
        var window = Open();

        Evaluate(window, "atan2(a: 1.5) |> out.left");
        Click(window, "atan2");

        // The second row, which is the second socket — the one the text says
        // nothing about.
        var slider = All<Slider>(window).ElementAt(1);

        slider.Value = 0.25d;
        Settle(window);

        Turn(window, 1.5d);

        Text(window).Text.Trim().ShouldBe("atan2(a: 1.5, b: 0.25) |> out.left");
    }
}
