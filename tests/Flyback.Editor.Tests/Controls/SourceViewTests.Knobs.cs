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
    // --- and a knob turned in it reaches the text ---------------------------

    /// <summary>A patch whose first module has knobs to turn.</summary>
    private static Patch Plasma() => Engine.Graph.Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

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
        over.RaiseEvent(new PointerReleasedEventArgs(
            over,
            new Pointer(0, PointerType.Mouse, true),
            over,
            default,
            0,
            default,
            KeyModifiers.None,
            MouseButton.Left)
        {
            RoutedEvent = InputElement.PointerReleasedEvent,
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
        var knob = All<Knob>(All<ControlsPanel>(window).Single()).Single(k => k.Name == "panel-knob");
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
        var window = Open(Plasma());
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
        var window = Open(Plasma());
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
            var key = Enum.Parse<PhysicalKey>($"Digit{c}");

            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyTextInput(c.ToString());
            window.KeyReleaseQwerty(key, RawInputModifiers.None);

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
}
