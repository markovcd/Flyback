using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Editor.Assist;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

public partial class SourceViewTests
{
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
            .ShouldAllBe(line => line.Length <= SourceLayout.Width);
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

        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.L,
            KeyModifiers = KeyModifiers.Control,
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

        var reading = PatchPrinter.Print(editor.History.Patch);

        // An edit on the canvas, before the text view has ever been opened, so
        // nothing writes it into a printing on the way.
        editor.History.Patch.Remove(editor.History.Patch.Nodes
            .First(node => node.TypeId != NodeCatalog.OutputTypeId).Id);

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
        var window = Open(Plasma());
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
            PatchPrinter.Print(Editor(window).History.Patch),
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
}
