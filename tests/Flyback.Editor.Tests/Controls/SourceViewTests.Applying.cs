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
        Press(text, KeyModifiers.Control);
        Settle(window);

        Editor(window).History.Patch.Nodes.Count.ShouldBe(3);

        // And a bare Enter is still the box's own, so typing a patch out over
        // several lines does not apply it four times on the way.
        var before = Editor(window).History.Patch;

        Press(text, KeyModifiers.None);
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
}
