using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The patch as text beside the patch as a graph, and which of the two is the
/// document.
/// </summary>
/// <remarks>
/// ADR-0068 settles it on the file rather than the view: a patch opened as a graph
/// is a graph, and the text view of it is a printing. Applying one is how somebody
/// deliberately takes a patch into text, and from then on the canvas is a view of
/// it. The window opens on a preset, so everything here starts from the canvas
/// owning the patch.
/// </remarks>
public class SourceViewTests : UiTest
{

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
        Editor(window).History.Locked.ShouldBeFalse();
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
    /// to say which line a complaint is about, and the language colored so a
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
    /// eight statement forms it is coloring.
    /// </summary>
    [AvaloniaFact]
    public void The_language_definition_names_the_words_the_language_has()
    {
        var window = Open();
        var colors = ShowCode(window).SyntaxHighlighting.ShouldNotBeNull();

        var named = colors.NamedHighlightingColors.Select(color => color.Name).ToList();

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

        var patch = Editor(window).History.Patch;

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

        Editor(window).History.Patch.Nodes.Count.ShouldBe(3);

        // And a bare Enter is still the box's own, so typing a patch out over
        // several lines does not apply it four times on the way.
        var before = Editor(window).History.Patch;

        Press(text, Avalonia.Input.KeyModifiers.None);
        Settle(window);

        Editor(window).History.Patch.ShouldBeSameAs(before);
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

        Editor(window).History.Locked.ShouldBeTrue();
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

        var applied = Editor(window).History.Patch.Nodes.Count;

        HandBack(window);

        Editor(window).History.Locked.ShouldBeFalse();
        Editor(window).IsVisible.ShouldBeTrue("the canvas is what somebody was trying to get to");
        Editor(window).History.Patch.Nodes.Count.ShouldBe(applied, "nothing was rebuilt to hand it over");

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

        Pick(Presets(window), "Kaleidoscope");
        Settle(window);

        Text(window).IsVisible.ShouldBeTrue("the text is the view the preset was picked from");
        Editor(window).IsVisible.ShouldBeFalse();

        // What it shows is the patch that has just arrived, and that text is now
        // the document rather than a printing offered for reading.
        Text(window).Text.ShouldNotBeNullOrWhiteSpace();
        Text(window).Text.ShouldNotBe(before);

        Editor(window).History.Locked.ShouldBeTrue("the text is the document, so the canvas is a view");
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

        Pick(Presets(window), "Kaleidoscope");
        Settle(window);

        Editor(window).History.IsModified.ShouldBeFalse();
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

        Pick(Presets(window), "Kaleidoscope");
        Settle(window);

        Undo(window).IsEnabled.ShouldBeFalse("nothing has been done to this patch yet");

        var read = Text(window).Text;
        var modules = Editor(window).History.Patch.Nodes.Count;

        // Pressed anyway, since the two stacks are what answer for the gesture
        // and a button that only looks off would still be answered by them.
        Press(Undo(window));
        Settle(window);

        Text(window).IsVisible.ShouldBeTrue("undo has nowhere to go, so the view does not move");
        Text(window).Text.ShouldBe(read);
        Editor(window).History.Patch.Nodes.Count.ShouldBe(modules);
        Editor(window).History.Locked.ShouldBeTrue("the text is still the document");
    }

    /// <summary>
    /// A preset picked from the canvas is still the graph's, which is the case
    /// ADR-0068 settled and this does not disturb.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_picked_from_the_canvas_is_still_the_graphs()
    {
        var window = Open();

        Pick(Presets(window), "Kaleidoscope");
        Settle(window);

        Editor(window).History.Locked.ShouldBeFalse();
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

        Editor(window).History.Locked.ShouldBeTrue("the question was refused, so nothing changed hands");
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
        var before = Editor(window).History.Patch.Nodes.Count;

        Evaluate(window, "t |> sine(freq: 220) |> out.leftt");

        Editor(window).History.Patch.Nodes.Count.ShouldBe(before);
        Editor(window).History.Locked.ShouldBeFalse("nothing was applied, so nothing changed hands");
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

        text.Text = "x |> sine(freq: 1.5) |> add(a: _, b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(hue: _, saturation: 0.85) |> gain(gain: 0.5) |> out.color";

        Press(Tidy(window));
        Settle(window);

        text.Text.ShouldContain("\n  |> ");
        text.Text.ReplaceLineEndings("\n").Split('\n')
            .ShouldAllBe(line => line.Length <= Core.Language.SourceLayout.Width);
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

        text.Text = "x |> sine(freq: 1.5) |> add(a: _, b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(hue: _, saturation: 0.85) |> gain(gain: 0.5) |> out.color";

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

        const string longLine = "x |> sine(freq: 1.5) |> add(a: _, b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(hue: _, saturation: 0.85) |> gain(gain: 0.5) |> out.color";

        text.Text = longLine;

        Press(Tidy(window));
        Settle(window);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(longLine);
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
    /// A run of typing comes back a word at a time, the way it does in every other
    /// editor.
    /// </summary>
    /// <remarks>
    /// The stack underneath takes an operation per change and a change is a
    /// keystroke, so a sentence used to come back one letter at a time. The space
    /// that ends a word belongs to the word, so a press leaves the line as it stood
    /// before that word.
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

        var applied = Editor(window).History.Patch.Nodes.Count;

        ShowCode(window).Text = Hum + "\n# and some more typing";
        Settle(window);

        CodeButton(window).IsChecked = false;
        Settle(window);

        Editor(window).History.Patch.Nodes.Count.ShouldBe(applied);

        Press(Undo(window));
        Settle(window);

        // Back to the preset the window opened on, which the evaluation replaced.
        Editor(window).History.Patch.Nodes.Count.ShouldNotBe(applied);
    }

    /// <summary>
    /// And an evaluation is a handover as much as an edit, so taking it back takes
    /// the handover back with it.
    /// </summary>
    /// <remarks>
    /// Undo an apply and what is on the canvas is a patch no text describes, so
    /// leaving the text as the document would lock it behind a printing of something
    /// else. Ctrl+Z was pressed to be back where the modules were, which is why the
    /// view goes back too.
    /// </remarks>
    [AvaloniaFact]
    public void Undoing_an_evaluation_gives_the_patch_back_to_the_canvas()
    {
        var window = Open();

        Evaluate(window, Hum);

        Editor(window).History.Locked.ShouldBeTrue("applying is how the text becomes the document");

        // Pressed at the text view, which is where applying leaves somebody.
        // Nothing was typed to make this printing, so the text has nothing of
        // its own to take back and the gesture is the patch's.
        Press(Undo(window));
        Settle(window);

        Editor(window).History.Locked.ShouldBeFalse("the evaluation that took it into text went back too");
        Editor(window).IsVisible.ShouldBeTrue("and the view went back with it");
        Notice(window).ShouldNotBeNull().ShouldContain("still the document");

        Press(Redo(window));
        Settle(window);

        Editor(window).History.Locked.ShouldBeTrue("and putting the evaluation back takes it into text");
        Editor(window).IsVisible.ShouldBeFalse("which is something done at the text view");
    }

    /// <summary>
    /// Redo does not repeat once it has put the evaluation back: there is one step to
    /// take back and one to put again.
    /// </summary>
    /// <remarks>
    /// A press that crosses the ownership boundary is answered by whichever stack the
    /// gesture lands on, so a rule that read the owner or the view instead could
    /// answer this redo from the canvas and leave the matching <c>Deed</c> stranded
    /// on the text's — which would look like a second redo on offer and do nothing.
    /// </remarks>
    [AvaloniaFact]
    public void Redo_does_not_repeat_after_putting_an_evaluation_back()
    {
        var window = Open();

        Evaluate(window, Hum);

        Press(Undo(window));
        Settle(window);

        Press(Redo(window));
        Settle(window);

        Editor(window).History.Locked.ShouldBeTrue("the evaluation came back with the first press");
        Redo(window).IsEnabled.ShouldBeFalse("there is nothing left to put again");

        Press(Redo(window));
        Settle(window);

        Editor(window).History.Locked.ShouldBeTrue("a second press had nothing to do and did nothing");
        Undo(window).IsEnabled.ShouldBeTrue("the evaluation is still one press back, not two");
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

        Editor(window).History.Locked.ShouldBeFalse();
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
        Editor(window).History.Locked.ShouldBeTrue("the typing was what there was to take back");

        Press(Undo(window));
        Settle(window);

        Editor(window).History.Locked.ShouldBeFalse("and now there is nothing but the handover");
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

        editor.History.Patch.Nodes[0].X += 40;
        editor.History.Record();
        Settle(window);

        Press(Undo(window));
        Settle(window);

        editor.History.Locked.ShouldBeFalse("the canvas was given the patch back and still has it");
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

        var reading = Core.Language.PatchPrinter.Print(editor.History.Patch);

        // An edit on the canvas, before the text view has ever been opened, so
        // nothing writes it into a printing on the way.
        editor.History.Patch.Remove(editor.History.Patch.Nodes
            .First(node => node.TypeId != Core.Graph.NodeCatalog.OutputTypeId).Id);

        editor.History.Record();
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
    /// What the write-back put in the text is the reading keeping up rather than an
    /// edit anybody made: answering Ctrl+Z with it would put the old number back over
    /// a patch still playing the new one, and leave the text no longer the printing
    /// it says it is.
    /// </remarks>
    [AvaloniaFact]
    public void Taking_back_a_knob_turned_over_a_printing_takes_back_the_knob()
    {
        var window = Open();
        var text = ShowCode(window);

        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        var chosen = Editor(window).Selection.Focused.ShouldNotBeNull();
        var was = chosen.InputValues[0];
        var reading = text.Text;

        Turn(window, 0.375d);

        text.Text.ShouldContain("0.375");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(reading, "the reading is made afresh from the patch that came back");
        Editor(window).History.Patch.Find(chosen.Id).ShouldNotBeNull().InputValues[0].ShouldBe(was);

        // And it is the printing again, so the caret still points the panel.
        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        Editor(window).Selection.Focused.ShouldNotBeNull().Id.ShouldBe(chosen.Id);
    }

    /// <summary>
    /// An undo that crosses no handover is not one, and says nothing about one.
    /// </summary>
    /// <remarks>
    /// An edit taken back while a printing happens to be showing changes nothing
    /// about who owns the patch. Announcing otherwise would be telling somebody about
    /// a switch they never made, and throwing the printing away would drop them back
    /// on the canvas they were looking away from.
    /// </remarks>
    [AvaloniaFact]
    public void Taking_back_a_canvas_edit_under_a_printing_is_not_a_handover()
    {
        var window = Open();
        var editor = Editor(window);

        // On the canvas, which is where the window opens and where it still is.
        editor.History.Patch.Nodes[0].X += 40;
        editor.History.Record();
        Settle(window);

        var text = ShowCode(window);

        Press(Undo(window));
        Settle(window);

        editor.History.Locked.ShouldBeFalse("the canvas had the patch all along");
        editor.IsVisible.ShouldBeFalse("and nobody asked to be taken off the text");
        Notice(window).ShouldNotBeNull().ShouldContain("still the document");

        // Still the printing it was, so the caret still points the panel at the
        // module under it — which a handover would have thrown away.
        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        editor.Selection.Focused.ShouldNotBeNull();
    }

    /// <summary>
    /// A printing applied as it stood and taken back is a printing again, and
    /// keeps up with the canvas.
    /// </summary>
    /// <remarks>
    /// Making a printing is noted beside the patch as it then stands, and not
    /// only beside the next step. The apply is recorded as a step back to there,
    /// so what Ctrl+Z hands back includes that the text on show is the window's
    /// own printing — not somebody's typing, which is never printed over.
    /// </remarks>
    [AvaloniaFact]
    public void A_printing_applied_and_taken_back_still_follows_the_canvas()
    {
        var window = Open();
        var text = ShowCode(window);

        Press(Apply(window));
        Settle(window);

        Press(Undo(window));
        Settle(window);

        Editor(window).History.Locked.ShouldBeFalse("the patch is the canvas's again");

        CodeButton(window).IsChecked = false;
        Settle(window);

        Editor(window).Edits.AddNode("value").ShouldNotBeNull();
        Settle(window);

        ShowCode(window);

        text.Text.ShouldBe(
            Core.Language.PatchPrinter.Print(Editor(window).History.Patch),
            "nothing was typed, so the text is a printing of what is on the canvas");
    }

    /// <summary>Everything the panel is saying, for the states where it says something.</summary>
    private static string Panel(MainWindow window) =>
        string.Join(" ", All<TextBlock>(Inspector(window)).Select(block => block.Text));

    /// <summary>
    /// The panel says why it has nothing, for a caret on a module the patch has moved
    /// on from.
    /// </summary>
    /// <remarks>
    /// The code names a module by where it stands, so a module typed in ahead of
    /// another renames that other one, and between the edit and the apply the names
    /// in the text are not the names on the canvas. A panel that went quiet there
    /// could not be told apart from a caret in the wrong place.
    /// </remarks>
    [AvaloniaFact]
    public void A_caret_on_a_module_the_patch_has_moved_on_from_says_so()
    {
        var window = Open();
        var text = ShowCode(window);

        Evaluate(window, "math.mix(a: 0.25) |> out.left");

        Click(window, "math.mix");
        Editor(window).Selection.Focused.ShouldNotBeNull("the caret points the panel to begin with");

        // Typed in ahead of it, which is what gives the Mix a new name.
        text.Document.Insert("math.mix(".Length, "b: _, ");
        text.Document.Insert(0, "t |> sine(freq: 2) |> ");
        Settle(window);

        Press(Apply(window));
        Settle(window);

        Click(window, "math.mix");
        Editor(window).Selection.Focused.ShouldNotBeNull("applied, so the two agree again");

        Press(Undo(window));
        Settle(window);

        Click(window, "math.mix");

        Editor(window).Selection.Focused.ShouldBeNull("the patch that came back has no such module");
        Panel(window).ShouldContain("moved on from the patch");

        // And the way out is said as well as the reason.
        Panel(window).ShouldContain("Apply");
    }

    /// <summary>
    /// And a word whose name the patch does know, but for a different module, is
    /// refused too.
    /// </summary>
    /// <remarks>
    /// The worse half of the same thing. A name that has moved from one module
    /// to another is still a name the patch has, so the panel filled with
    /// somebody else's knobs and said nothing about it — the caret on the module
    /// that was typed in pointed at the one it renamed.
    /// </remarks>
    [AvaloniaFact]
    public void A_name_that_now_means_another_module_is_refused_as_well()
    {
        var window = Open();
        var text = ShowCode(window);

        Evaluate(window, "math.mix(a: 0.25) |> out.left");

        text.Document.Insert("math.mix(".Length, "b: _, ");
        text.Document.Insert(0, "t |> sine(freq: 2) |> ");
        Settle(window);

        Press(Apply(window));
        Settle(window);

        Press(Undo(window));
        Settle(window);

        // The sine stands where the Mix stood, so it carries the name the
        // patch still has for the Mix.
        Click(window, "sine");

        Editor(window).Selection.Focused.ShouldBeNull("that name means another module now");
        Panel(window).ShouldContain("moved on from the patch");
    }

    /// <summary>
    /// An edit that renames nothing leaves the panel following as it was.
    /// </summary>
    /// <remarks>
    /// Which is most editing. A number changed where the text already says it
    /// moves no module, so every name in the text is still the name on the
    /// canvas and there is nothing to warn about — a panel that stopped for this
    /// would stop for everything.
    /// </remarks>
    [AvaloniaFact]
    public void An_edit_that_renames_nothing_leaves_the_panel_following()
    {
        var window = Open();
        var text = ShowCode(window);

        Evaluate(window, "math.mix(a: 0.25) |> out.left");

        text.Document.Replace(text.Text.IndexOf("0.25", StringComparison.Ordinal), 4, "0.75");
        Settle(window);

        Press(Apply(window));
        Settle(window);

        Press(Undo(window));
        Settle(window);

        Click(window, "math.mix");

        Editor(window).Selection.Focused.ShouldNotBeNull("nothing was renamed, so the name still means it");
    }

    /// <summary>The one knob the patches used here have.</summary>
    private static float Knob(MainWindow window) =>
        Editor(window).History.Patch.Nodes.Single(node => node.TypeId == "math.mix").InputValues[0];

    /// <summary>
    /// Applying is a thing done to the document, so it goes on the document's stack
    /// beside the typing that led to it — and being the last thing done, it is the
    /// first thing back.
    /// </summary>
    /// <remarks>
    /// Kept apart, Ctrl+Z after an apply took back a line typed some minutes earlier
    /// and left the patch it had already been built into where it was.
    /// </remarks>
    [AvaloniaFact]
    public void An_apply_comes_back_before_the_typing_that_led_to_it()
    {
        var window = Open();

        Evaluate(window, "math.mix(a: 0.25) |> out.left");

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

        text.Text.Trim().ShouldBe("math.mix(a: 0.25) |> out.left");
    }

    /// <summary>
    /// And a knob turned in the panel is one thing done as well, however little of it
    /// is text: the number in the code and the value behind it come back in one press.
    /// </summary>
    /// <remarks>
    /// Taking back only the text would leave the file reading 1.5524476 over a patch
    /// still playing 2, until the next apply quietly undid the knob as a side effect
    /// of building something else.
    /// </remarks>
    [AvaloniaFact]
    public void Undoing_a_knob_turned_in_the_panel_takes_the_value_back_too()
    {
        var window = Open();

        Evaluate(window, "math.mix(a: 1.5524476) |> out.left");
        Click(window, "math.mix");

        Turn(window, 2d);

        Text(window).Text.Trim().ShouldBe("math.mix(a: 2) |> out.left");
        Knob(window).ShouldBe(2f);

        Press(Undo(window));
        Settle(window);

        Text(window).Text.Trim().ShouldBe("math.mix(a: 1.5524476) |> out.left");
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

        editor.Selection.SelectAll();
        editor.Focus();

        var before = editor.History.Patch.Nodes.Count;

        window.KeyPress(
            Avalonia.Input.Key.Delete,
            Avalonia.Input.RawInputModifiers.None,
            Avalonia.Input.PhysicalKey.Delete,
            null);
        Settle(window);

        editor.History.Patch.Nodes.Count.ShouldBe(before);
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

        editor.Selection.SelectAll();
        editor.Selection.Nodes.Count.ShouldBe(editor.History.Patch.Nodes.Count);
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
    /// On a group's header, or anywhere in its block that is about no module, the
    /// caret points the panel at the group, as a click on the box does; on a
    /// module inside it, at the module.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_on_a_group_points_the_panel_at_the_group()
    {
        var window = Open();

        Evaluate(window,
            """
            group "Voice" {
              let a = sine(freq: 2)

              let b = a |> drive()
            }
            b |> out.left
            """);

        Click(window, "group \"Voice\"");

        Editor(window).Selection.Group.ShouldNotBeNull().Name.ShouldBe("Voice");
        Panel(window).ShouldContain("drawn as one");

        Click(window, "sine");
        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");

        // The blank line between the two bindings, inside the block.
        var text = Text(window);
        text.CaretOffset = text.Text.IndexOf("let b", StringComparison.Ordinal) - 3;
        Settle(window);

        Editor(window).Selection.Group.ShouldNotBeNull().Name.ShouldBe("Voice");
    }

    /// <summary>
    /// A group named in the text since it was applied is not on the canvas yet,
    /// and the panel says so in a group's words rather than falling silent.
    /// </summary>
    [AvaloniaFact]
    public void A_group_the_canvas_has_not_got_yet_says_so()
    {
        var window = Open();

        Evaluate(window,
            """
            group "Voice" {
              let a = sine(freq: 2)
              let b = a |> drive()
            }
            b |> filter(cutoff: 400) |> out.left
            """);

        var text = Text(window);
        text.Document.Replace(text.Text.IndexOf("Voice", StringComparison.Ordinal), 5, "Chorus");
        Settle(window);

        Click(window, "group \"Chorus\"");

        Editor(window).Selection.Focused.ShouldBeNull();
        Panel(window).ShouldContain("this group is not there to show yet");

        // Renaming a group moves the modules in it too, but not one outside it.
        Click(window, "filter");
        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("audio.filter");
    }

    /// <summary>
    /// The caret is the text view's pointer, and the panel follows it exactly as
    /// it follows a click on the canvas.
    /// </summary>
    [AvaloniaFact]
    public void The_caret_points_the_panel()
    {
        var window = Open();

        Evaluate(window, Hum);
        Click(window, "sine");

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
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

        Evaluate(window, "math.mix(a: 1.5524476) |> out.left");
        Click(window, "math.mix");

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("math.mix");
    }

    /// <summary>
    /// A printing is what the text view shows for a patch built on the canvas,
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

        Editor(window).Selection.Focused.ShouldNotBeNull();
    }

    // --- and a knob turned in it reaches the text ---------------------------

    /// <summary>Turns the first knob the panel is showing, and lets go of it.</summary>
    private static void Turn(MainWindow window, double to)
    {
        var slider = Knobs(window).First();

        // The first knob's slider, which on a socket with a knee is travel rather than value.
        var node = Editor(window).Selection.Focused.ShouldNotBeNull();
        var spec = NodeCatalog.Require(node.TypeId).Inputs.First(p => NodeCatalog.Normalled(p) is null && !p.NeedsAWire);

        slider.Value = spec.Knee > 0f ? spec.Travel((float)to, spec.Min, spec.Max) : to;
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

        Evaluate(window, "math.mix(a: 1.5524476) |> out.left");
        Click(window, "math.mix");

        Turn(window, 2d);

        Text(window).Text.Trim().ShouldBe("math.mix(a: 2) |> out.left");
    }

    /// <summary>
    /// A panel knob turned by hand rests where it was left, and the <c>panel</c>
    /// line says so, or applying the text again would put it back.
    /// </summary>
    [AvaloniaFact]
    public void A_panel_knob_turned_by_hand_is_written_into_its_panel_line()
    {
        var window = Open();

        Evaluate(window, "panel level = 0.5, cc: 7, device: \"midi:test\"\nsine(freq: 220, amp: level) |> out.left");
        TurnPanelKnob(window, 40);

        var rests = Editor(window).History.Patch.Controls.ShouldNotBeNull().Single().Value;

        rests.ShouldBeGreaterThan(0.5f);
        Text(window).Text.ShouldStartWith($"panel level = {PatchPrinter.Knob(rests, PortDisplay.Number)}, cc: 7,");
    }

    /// <summary>The same over a printing, which stays a true reading of the canvas.</summary>
    [AvaloniaFact]
    public void A_panel_knob_turned_over_a_printing_keeps_the_printing_true()
    {
        var window = Open();

        Evaluate(window, "panel level = 0.5\nsine(freq: 220, amp: level) |> out.left");
        HandBack(window);

        var text = ShowCode(window);

        TurnPanelKnob(window, 40);

        var rests = Editor(window).History.Patch.Controls.ShouldNotBeNull().Single().Value;

        text.Text.ShouldContain($"panel level = {PatchPrinter.Knob(rests, PortDisplay.Number)}");
        text.Text.ShouldBe(PatchPrinter.Print(Editor(window).History.Patch));
    }

    /// <summary>
    /// Moving, removing and adding a knob on the panel is said by the text's
    /// <c>panel</c> lines, and nothing else in the text is touched.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_moved_removed_or_added_on_the_panel_rewrites_the_panel_lines()
    {
        var window = Open();

        Evaluate(window, "# the knobs\npanel level = 0.5\npanel tone = 0.25\npanel spare = 0.75\nsine(freq: 220, amp: level) |> out.left\nout.volume = tone");

        KnobMenu(window, 0, "Move right");
        Text(window).Text.ShouldStartWith("# the knobs\npanel tone = 0.25\npanel level = 0.5\npanel spare = 0.75\n");

        KnobMenu(window, 2, "Remove knob");
        Text(window).Text.ShouldStartWith("# the knobs\npanel tone = 0.25\npanel level = 0.5\nsine(");

        var add = All<Button>(window).Single(b => b.Name == "add-knob");
        var at = add.TranslatePoint(new Point(add.Bounds.Width / 2, add.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        Text(window).Text.ShouldStartWith("# the knobs\npanel tone = 0.25\npanel level = 0.5\npanel knob_1 = 0.5, label: \"Knob 1\"\nsine(");
        PatchLanguage.Build(Text(window).Text).Issues.ShouldBeEmpty();
    }

    /// <summary>The same over a printing, which is printed again.</summary>
    [AvaloniaFact]
    public void A_knob_moved_on_the_panel_over_a_printing_reprints_it()
    {
        var window = Open();

        Evaluate(window, "panel level = 0.5\npanel tone = 0.25\nsine(freq: 220, amp: level) |> out.left\nout.volume = tone");
        HandBack(window);

        var text = ShowCode(window);

        KnobMenu(window, 0, "Move right");

        text.Text.ShouldStartWith("panel tone = 0.25\npanel level = 0.5\n");
        text.Text.ShouldBe(PatchPrinter.Print(Editor(window).History.Patch));
    }

    private static void KnobMenu(MainWindow window, int knob, string item)
    {
        var more = All<Button>(All<ControlsPanel>(window).Single()).Where(b => b.Name == "knob-menu").ElementAt(knob);
        var menu = more.Flyout.ShouldBeOfType<MenuFlyout>();

        menu.Items.OfType<MenuItem>().Single(i => (i.Header as string) == item)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);
    }

    private static void TurnPanelKnob(MainWindow window, double up)
    {
        var knob = All<Knob>(All<ControlsPanel>(window).Single()).Single();
        var from = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), window)!.Value;

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from - new Point(0, up));
        window.MouseUp(from - new Point(0, up), MouseButton.Left);
        Settle(window);
    }

    /// <summary>
    /// A drag is one gesture and one edit. Writing per frame would put a hundred
    /// things on the undo stack and flicker the line under whoever is reading it.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_still_being_dragged_has_not_reached_the_text_yet()
    {
        var window = Open();

        Evaluate(window, "math.mix(a: 1.5524476) |> out.left");
        Click(window, "math.mix");

        var slider = Knobs(window).First();

        slider.Value = 2d;
        Settle(window);

        Text(window).Text.ShouldContain("1.5524476");

        // And the engine has it all the same, which is the point of turning a
        // knob while a patch is playing.
        Editor(window).Selection.Focused.ShouldNotBeNull().InputValues[0].ShouldBe(2f);
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

        var chosen = Editor(window).Selection.Focused.ShouldNotBeNull();
        var before = text.Text;

        Turn(window, 0.375d);

        text.Text.ShouldNotBe(before, "the printing has to keep up with the knob");
        text.Text.ShouldContain("0.375");

        // Still the canvas's patch, and still pointed at: a printing written
        // into by this is a printing still.
        Editor(window).History.Locked.ShouldBeFalse();
        Notice(window).ShouldNotBeNull();

        text.CaretOffset = 0;
        text.CaretOffset = text.Text.IndexOf('(');
        Settle(window);

        Editor(window).Selection.Focused.ShouldNotBeNull().Id.ShouldBe(chosen.Id);
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

        var chosen = Editor(window).Selection.Focused.ShouldNotBeNull();

        Turn(window, 0.375d);

        Editor(window).Selection.Focused
            .ShouldNotBeNull("the panel emptied itself while the knob was being let go of")
            .Id.ShouldBe(chosen.Id);

        // And the caret still points the panel afterwards, rather than the map
        // being left empty for the rest of the session.
        var second = text.Text.IndexOf('(', text.Text.IndexOf(')'));

        second.ShouldBeGreaterThan(0, "the preset prints as more than one call");

        text.CaretOffset = second;
        Settle(window);

        Editor(window).Selection.Focused.ShouldNotBeNull("the caret stopped pointing the panel");
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

        Release(Knobs(window).First());
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

        Evaluate(window, "math.mix(a: 1.5) |> out.left");
        Click(window, "math.mix");

        // The second row, which is the second socket — the one the text says
        // nothing about.
        var slider = Knobs(window).ElementAt(1);

        slider.Value = 0.25d;
        Settle(window);

        Turn(window, 1.5d);

        Text(window).Text.Trim().ShouldBe("math.mix(a: 1.5, b: 0.25) |> out.left");
    }

    /// <summary>Types digits into whatever has the focus, the way a keyboard does.</summary>
    /// <remarks>
    /// Three events per character and not one: a box takes the character on the
    /// text input between the press and the release, so a test that raised only
    /// one of the three would be checking an order the keyboard does not have.
    /// </remarks>
    private static void TypeDigits(MainWindow window, string what)
    {
        foreach (var c in what)
        {
            var key = Enum.Parse<Avalonia.Input.PhysicalKey>($"Digit{c}");

            window.KeyPressQwerty(key, Avalonia.Input.RawInputModifiers.None);
            window.KeyTextInput(c.ToString());
            window.KeyReleaseQwerty(key, Avalonia.Input.RawInputModifiers.None);

            Settle(window);
        }
    }

    /// <summary>
    /// A number box takes what is typed as it is typed, so the text keeps up
    /// keystroke by keystroke rather than waiting for the box to be let go of.
    /// </summary>
    /// <remarks>
    /// Nothing here lets go of a pointer or moves the focus, which were the two things
    /// the write-back waited for — so somebody typing a note saw a text view showing
    /// the number the patch had already stopped playing.
    /// </remarks>
    [AvaloniaFact]
    public void A_number_typed_in_the_panel_reaches_the_text_without_leaving_the_box()
    {
        var window = Open();

        Evaluate(window, "let riff = notes() [ A3 C4 ]\nriff |> out.left");
        Click(window, "notes");

        // The box holding the first step, found by what it holds: A3 is 57 and
        // no knob on a sequencer rests there.
        var box = All<TextBox>(All<NumericUpDown>(window).First(n => n.Value == 57m)).First();

        box.Focus();
        box.SelectAll();
        Settle(window);

        TypeDigits(window, "6");

        // Half a number typed is still what the patch is playing, and the text
        // says what is playing.
        Text(window).Text.ShouldNotContain("A3");

        TypeDigits(window, "0");

        Text(window).Text.Trim().ShouldBe("let riff = notes() [ C4 C4 ]\nriff |> out.left");

        // And none of that was the box being left: it is still the thing being
        // typed into, which is the whole point of the test.
        window.FocusManager?.GetFocusedElement().ShouldBe(box);
    }

    /// <summary>
    /// A notch of the wheel over a number box moves it, and the text keeps up
    /// with that as it keeps up with a keystroke.
    /// </summary>
    /// <remarks>
    /// The third way into a box and the third that lets go of nothing: the
    /// button is never pressed, so there is no release to wait for, and the box
    /// keeps the focus it was given. What made this worth a test of its own is
    /// that it is the one of the three that does not touch the keyboard at all.
    /// </remarks>
    [AvaloniaFact]
    public void A_number_wheeled_in_the_panel_reaches_the_text_without_leaving_the_box()
    {
        var window = Open();

        Evaluate(window, "let riff = notes() [ A3 C4 ]\nriff |> out.left");
        Click(window, "notes");

        var step = All<NumericUpDown>(window).First(n => n.Value == 57m);
        var box = All<TextBox>(step).First();

        // A box only spins under the wheel while it has the focus, which is what
        // a pointer over it has already given it by the time anybody scrolls.
        box.Focus();
        Settle(window);

        var at = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the box is not in this window");

        window.MouseWheel(at, new Vector(0, 1));
        Settle(window);

        step.Value.ShouldBe(58m, "a notch of the wheel is what moves a number box");

        Text(window).Text.Trim().ShouldBe("let riff = notes() [ A#3 C4 ]\nriff |> out.left");
    }

    // --- which view a press of undo belongs to ------------------------------

    /// <summary>
    /// Undo follows the view where the canvas owns the patch. Typing into a printing
    /// is on the text's stack, and with the canvas showing it is typing nobody can
    /// see — a press there takes back the last thing done to the canvas.
    /// </summary>
    [AvaloniaFact]
    public void Undo_on_the_canvas_leaves_typing_in_a_hidden_printing_alone()
    {
        var window = Open();
        var text = ShowCode(window);

        // Through the document, which is what typing is.
        text.Document.Insert(0, "# a note to myself\n");
        Settle(window);

        CodeButton(window).IsChecked = false;
        Settle(window);

        var before = Editor(window).History.Patch.Nodes.Count;

        Editor(window).Edits.AddNode("value").ShouldNotBeNull();
        Settle(window);

        Press(Undo(window));
        Settle(window);

        Editor(window).History.Patch.Nodes.Count.ShouldBe(before, "the module just added is what came back");
        text.Text.ShouldStartWith("# a note to myself", customMessage: "and the typing was left alone");

        // With nothing left on the canvas's stack the button goes gray, rather
        // than offering a press that would land where nobody is looking.
        Undo(window).IsEnabled.ShouldBeFalse();

        // The typing is still there to take back from the view it was done in.
        ShowCode(window);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldNotStartWith("# a note to myself");
    }

    /// <summary>
    /// A knob turned on the canvas leaves typing in the hidden printing its
    /// undo.
    /// </summary>
    /// <remarks>
    /// Writing a knob back into a printing forgets the text's steps, since the
    /// reading is made afresh and one of them would put the old number back. A
    /// printing that has been typed into is not written to, and the steps on
    /// its stack are the typing, so those are kept.
    /// </remarks>
    [AvaloniaFact]
    public void A_knob_turned_on_the_canvas_leaves_hidden_typing_its_undo()
    {
        var window = Open();
        var text = ShowCode(window);

        // Through the document, which is what typing is.
        text.Document.Insert(0, "# a note to myself\n");
        Settle(window);

        CodeButton(window).IsChecked = false;
        Settle(window);

        var editor = Editor(window);
        var turned = editor.History.Patch.Nodes.First(n =>
            n.TypeId != Core.Graph.NodeCatalog.OutputTypeId
            && Core.Graph.NodeCatalog.BuiltIn.Require(n.TypeId).Inputs.Count > 0);

        editor.Selection.Select(turned.Id);
        Settle(window);

        var slider = Knobs(window).First();

        Turn(window, slider.Minimum + ((slider.Maximum - slider.Minimum) * 0.37));

        ShowCode(window);

        text.Text.ShouldStartWith("# a note to myself");

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldNotStartWith("# a note to myself", customMessage: "the typing is what Ctrl+Z takes back in the view it was done in");
    }

    // --- a number no box can hold -------------------------------------------

    /// <summary>
    /// A knob is a float and its number box holds a decimal, which stops near
    /// 7.9e28. Applying points the panel at the caret, so text holding a larger
    /// number has to survive the panel being built for it.
    /// </summary>
    [AvaloniaFact]
    public void A_number_past_what_a_number_box_holds_can_be_applied()
    {
        var window = Open();
        var text = ShowCode(window);

        text.Text = "t |> sine(freq: 100000000000000000000000000000000) |> out.left";
        text.CaretOffset = text.Text.IndexOf("sine", StringComparison.Ordinal) + 1;
        Settle(window);

        Should.NotThrow(() =>
        {
            Apply(window).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
        });

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
    }

    // --- a locked canvas, and a box on it --------------------------------------

    /// <summary>
    /// A double-click looks into a box rather than opening it, so a locked canvas does
    /// it too: nothing in the patch changes.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_canvas_looks_into_a_box_on_a_double_click()
    {
        var window = Open();

        Evaluate(window, """
            group "Voice" {
              let a = t |> sine(freq: 220)
              let b = a |> mul(a: _, b: 0.5)
            }
            b |> out.left
            """);

        var editor = Editor(window);

        editor.History.Locked.ShouldBeTrue();

        CodeButton(window).IsChecked = false;
        Settle(window);

        var group = editor.History.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        group.Collapsed.ShouldBeTrue("a group built from text arrives shut");

        var box = Geometry.GroupBounds(editor.History.Patch, group, editor.History.Patch.SocketsOf(group));

        var at = editor.TranslatePoint(
                editor.GraphToScreen.Transform(new Point(box.X + (box.Width / 2), box.Y + 8)), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        var steps = 0;
        editor.History.Recorded += (_, _) => steps++;

        for (var click = 0; click < 2; click++)
        {
            window.MouseDown(at, Avalonia.Input.MouseButton.Left);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        }

        Settle(window);

        group.Collapsed.ShouldBeTrue();
        steps.ShouldBe(0, "nothing done to a locked canvas is an edit to the patch");
        editor.Selection.Peeked.ShouldBe(group);
        editor.Selection.Group.ShouldBe(group, "the press still selects what the box stands for");
    }

    // --- an assistant's patch, and the text ---------------------------------

    /// <summary>
    /// What the window handed the assistant panel to put a patch on the canvas with,
    /// which is the one thing the end of a turn does to the shell.
    /// </summary>
    /// <remarks>
    /// By reflection: the window takes its plugins from a static no test can put a
    /// provider into, so no turn can be run against a real window.
    /// </remarks>
    private static Action<Core.Graph.Patch> AssistantApplies(MainWindow window)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        return (Action<Core.Graph.Patch>)typeof(AssistantPanel)
            .GetField("apply", flags)!
            .GetValue(All<AssistantPanel>(window).Single())!;
    }

    /// <summary>Two oscillators mixed into the left speaker: a patch no text here describes.</summary>
    private static Core.Graph.Patch Drone()
    {
        var b = new Core.Graph.PatchBuilder(Core.Graph.NodeCatalog.BuiltIn);

        var output = b.Add(Core.Graph.NodeCatalog.OutputTypeId, 900, 40);
        var one = b.Add("osc.sine", 40, 40);
        var two = b.Add("osc.sine", 40, 240);
        var mix = b.Add("math.add", 400, 40);

        b.Wire(one, 0, mix, 0)
            .Wire(two, 0, mix, 1)
            .Wire(mix, 0, output, Core.Graph.NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static int Oscillators(Core.Graph.Patch patch) =>
        patch.Nodes.Count(n => n.TypeId == "osc.sine");

    /// <summary>
    /// A printing keeps up with an assistant's patch as it keeps up with an undo.
    /// The assistant column is beside the text view, so a turn ending over a
    /// printing is ordinary — and applying a stale one would put the old patch back.
    /// </summary>
    [AvaloniaFact]
    public void A_printing_keeps_up_with_an_assistants_patch()
    {
        var window = Open();
        var text = ShowCode(window);

        AssistantApplies(window)(Drone());
        Settle(window);

        Editor(window).History.Locked.ShouldBeFalse("a printing is a reading; the canvas still owns the patch");
        Notice(window).ShouldNotBeNull();

        Oscillators(Core.Language.PatchLanguage.Build(text.Text).Patch).ShouldBe(2);
    }

    /// <summary>
    /// Where the text is the document it goes on saying what the canvas holds, since
    /// it is what a save writes and what the next apply builds: the assistant's
    /// patch is written into it and built from there.
    /// </summary>
    [AvaloniaFact]
    public void An_assistants_patch_over_a_text_document_is_written_into_the_text()
    {
        var window = Open();

        Evaluate(window, Hum);

        AssistantApplies(window)(Drone());
        Settle(window);

        var editor = Editor(window);

        editor.History.Locked.ShouldBeTrue("the text is still the document");
        Oscillators(editor.History.Patch).ShouldBe(2, "the assistant's patch is on the canvas");
        Oscillators(Core.Language.PatchLanguage.Build(Text(window).Text).Patch).ShouldBe(2);
    }

    /// <summary>
    /// And one press takes both back: the patch, and the text as it was written —
    /// the comment in it included, which no printing could have kept.
    /// </summary>
    [AvaloniaFact]
    public void Undoing_an_assistants_patch_puts_back_the_text_as_it_was_written()
    {
        var window = Open();

        Evaluate(window, Hum);

        var text = Text(window);

        // Typed rather than loaded, so there is a stack under the text to go back
        // down — and a second apply, so the patch is this text's.
        text.Document.Insert(text.Document.TextLength, "\n# and a second note");
        Apply(window).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        var written = text.Text;

        AssistantApplies(window)(Drone());
        Settle(window);

        text.Text.ShouldNotBe(written);

        Press(Undo(window));
        Settle(window);

        text.Text.ShouldBe(written);
        Oscillators(Editor(window).History.Patch).ShouldBe(1, "the patch the text describes is back with it");
        Editor(window).History.Locked.ShouldBeTrue();
    }
}
