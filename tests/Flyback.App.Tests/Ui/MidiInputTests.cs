using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The MIDI In as it is actually used: picked in the panel, played from the
/// letters, and — the part with the most to go wrong — not played from the
/// letters when they are meant for something else.
/// </summary>
/// <remarks>
/// The engine's tests cover what the module compiles to and the hub's cover note
/// priority. What only a window can answer is whether a keystroke reaches the
/// instrument or the editor, because that question is about focus and about which
/// program is running, and neither exists below this layer.
/// </remarks>
public class MidiInputTests : UiTest
{
    private static MainWindow Open(Patch patch)
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        All<NodeEditor>(window).Single().Patch = patch;
        Settle(window);

        return window;
    }

    /// <summary>A MIDI In whose pitch reaches the picture, and the Output it feeds.</summary>
    private static (Patch Patch, NodeInstance Midi) Board(bool wired = true)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var midi = b.Add(NodeCatalog.MidiTypeId, 200, 40);

        if (wired) b.Wire(midi, 0, output, NodeCatalog.OutputColorPort);

        return (b.Patch, midi);
    }

    private static void Select(MainWindow window, NodeInstance node)
    {
        var editor = All<NodeEditor>(window).Single();
        var at = editor.TranslatePoint(
            editor.GraphToScreen.Transform(
                new Point(
                    node.X + NodeGeometry.Width / 2,
                    node.Y + NodeGeometry.HeaderHeight / 2)),
            window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    /// <summary>What the picture is currently reading, by name.</summary>
    private static IReadOnlyList<string> Drawn(MainWindow window) =>
        All<PreviewHost>(window).Single().Program.LiveInputs;

    [AvaloniaFact]
    public void The_module_is_in_the_catalogue_under_Source()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.MidiTypeId);

        def.Category.ShouldBe("Source");
        def.Inputs.ShouldBeEmpty();
        def.Outputs.Select(port => port.Name).ShouldBe(["pitch", "gate", "velocity", "trigger"]);
    }

    /// <summary>
    /// The panel offers a list rather than a number or a switch, which is the
    /// third field shape and the reason it exists.
    /// </summary>
    [AvaloniaFact]
    public void The_panel_offers_a_list_of_instruments_to_pick_from()
    {
        var (patch, midi) = Board();
        var window = Open(patch);

        Select(window, midi);

        var picker = All<ComboBox>(window)
            .FirstOrDefault(box => box.ItemsSource?.Cast<object>().Any(o => o is ChoiceOption) == true);

        picker.ShouldNotBeNull();

        picker.ItemsSource!.Cast<ChoiceOption>()
            .ShouldContain(option => option.Id == MidiSources.Keyboard);

        // And it opens on what the module is actually listening to rather than on
        // nothing, which is what a fresh instance carries.
        ((ChoiceOption)picker.SelectedItem!).Id.ShouldBe(MidiSources.Keyboard);
    }

    [AvaloniaFact]
    public void A_patch_holding_one_is_compiled_to_read_the_keyboard()
    {
        var (patch, _) = Board();
        var window = Open(patch);

        Drawn(window).ShouldContain(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Pitch));
    }

    /// <summary>
    /// Wired to nothing it is read by neither program, so it is not listening and
    /// the letters go on meaning what they meant to the editor.
    /// </summary>
    [AvaloniaFact]
    public void One_wired_to_nothing_asks_for_nothing()
    {
        var (patch, _) = Board(wired: false);
        var window = Open(patch);

        Drawn(window).ShouldBeEmpty();
    }

    /// <summary>
    /// A letter plays a note, and having played one is taken — so it does not also
    /// mean whatever it meant to the canvas.
    /// </summary>
    [AvaloniaFact]
    public void A_letter_plays_a_note_while_something_is_listening()
    {
        var (patch, _) = Board();
        var window = Open(patch);
        var preview = All<PreviewHost>(window).Single();

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);

        Held(preview, MidiSignal.Pitch).ShouldBe(48d);
        Held(preview, MidiSignal.Gate).ShouldBe(1d);

        window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(0d);
    }

    /// <summary>
    /// The same letter, on a patch nothing is listening with. Nothing is played,
    /// and nothing about the editor changes.
    /// </summary>
    [AvaloniaFact]
    public void A_letter_plays_nothing_when_no_module_is_listening()
    {
        var (patch, _) = Board(wired: false);
        var window = Open(patch);
        var preview = All<PreviewHost>(window).Single();

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);

        preview.Live.Count.ShouldBe(0);
    }

    /// <summary>
    /// A note held while the patch is edited must still be held after it. Every
    /// edit recompiles, and every recompile makes new blocks.
    /// </summary>
    [AvaloniaFact]
    public void A_note_survives_the_recompile_an_edit_causes()
    {
        var (patch, midi) = Board();
        var window = Open(patch);
        var editor = All<NodeEditor>(window).Single();
        var preview = All<PreviewHost>(window).Single();

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);

        // Any edit at all: what matters is that the patch is compiled again.
        editor.Patch.Nodes.First(n => n.Id == midi.Id).X += 10;
        editor.NotifyPatchChanged("moved");
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(1d);
        Held(preview, MidiSignal.Pitch).ShouldBe(48d);
    }

    /// <summary>
    /// The shortcuts that land on letters the layout also plays. Z, Y and L are
    /// undo, redo and lay out; C, X, V and A are the clipboard and select-all;
    /// and every one of them is a note. A note is a bare keystroke and nothing
    /// else, so holding Ctrl is what tells the two apart.
    /// </summary>
    /// <remarks>
    /// Checked by playing nothing rather than by watching the shortcut work: what
    /// each of them does is somebody else's test, and what is new here is that
    /// they still get the chance to. The gate is the whole of the evidence — if
    /// the keystroke had been taken as a note it would be open, and the shortcut
    /// would have been marked handled before anything else saw it.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(PhysicalKey.Z)]
    [InlineData(PhysicalKey.Y)]
    [InlineData(PhysicalKey.L)]
    [InlineData(PhysicalKey.C)]
    [InlineData(PhysicalKey.X)]
    [InlineData(PhysicalKey.V)]
    [InlineData(PhysicalKey.A)]
    [InlineData(PhysicalKey.F)]
    public void A_shortcut_on_a_letter_is_still_a_shortcut(PhysicalKey key)
    {
        var (patch, _) = Board();
        var window = Open(patch);
        var preview = All<PreviewHost>(window).Single();

        window.KeyPressQwerty(key, RawInputModifiers.Control);
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(0d);
    }

    /// <summary>
    /// Framing moved off a bare letter and under Ctrl with the rest of the
    /// editor's gestures, so that every bare letter belongs to the instrument.
    /// </summary>
    /// <remarks>
    /// F was not a note under the layout that ships, so this was not a conflict
    /// yet. It moved anyway: a gesture that worked only until somebody added a
    /// key to the layout is worse than one that reads by the same rule as its
    /// neighbours.
    /// </remarks>
    [AvaloniaFact]
    public void Ctrl_F_frames_the_patch_and_F_alone_no_longer_does()
    {
        var (patch, midi) = Board();
        var window = Open(patch);
        var editor = All<NodeEditor>(window).Single();

        // The canvas focused, so that its own key handling is reached at all.
        // Opening a patch frames it, so one is dragged a long way off first —
        // otherwise there is nothing for framing to do and a gesture that did
        // nothing would look exactly like one that worked.
        editor.Focus();
        editor.Patch.Nodes.First(n => n.Id == midi.Id).X += 2400;
        editor.NotifyPatchChanged("moved a long way");
        Settle(window);

        var stale = editor.GraphToScreen.Transform(new Point(0, 0));

        // The letter on its own is a note now, and leaves the view alone.
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
        Settle(window);

        editor.GraphToScreen.Transform(new Point(0, 0))
            .ShouldBe(stale, "F alone should no longer frame anything");

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Settle(window);

        editor.GraphToScreen.Transform(new Point(0, 0))
            .ShouldNotBe(stale, "Ctrl+F should have framed the patch");
    }

    /// <summary>
    /// Undo in particular, all the way through: Ctrl+Z on a letter that is also a
    /// note has to take an edit back rather than sound one.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Z_undoes_while_the_patch_is_being_played()
    {
        var (patch, midi) = Board();
        var window = Open(patch);
        var editor = All<NodeEditor>(window).Single();

        var moved = editor.Patch.Nodes.First(n => n.Id == midi.Id);
        var was = moved.X;

        moved.X += 120;
        editor.NotifyPatchChanged("moved");
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Settle(window);

        editor.Patch.Nodes.First(n => n.Id == midi.Id).X.ShouldBe(was);
        Held(All<PreviewHost>(window).Single(), MidiSignal.Gate).ShouldBe(0d);
    }

    /// <summary>
    /// Shift is not a command modifier. No gesture in the shell is Shift and a
    /// letter, so a capital Z is still a Z and still plays — which also keeps
    /// Ctrl+Shift+Z reaching redo, since that one carries Ctrl as well.
    /// </summary>
    [AvaloniaFact]
    public void A_capital_letter_still_plays()
    {
        var (patch, _) = Board();
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Shift);
        Settle(window);

        Held(All<PreviewHost>(window).Single(), MidiSignal.Gate).ShouldBe(1d);
    }

    /// <summary>
    /// A modifier taken hold of while a note is sounding must not strand it. The
    /// release is the one keystroke that carries no guards at all, for exactly
    /// this: every guard in front of it is a way for a note to be missed, and a
    /// missed release lasts the rest of the session.
    /// </summary>
    [AvaloniaFact]
    public void A_note_let_go_under_a_modifier_still_stops()
    {
        var (patch, _) = Board();
        var window = Open(patch);
        var preview = All<PreviewHost>(window).Single();

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);
        Held(preview, MidiSignal.Gate).ShouldBe(1d);

        window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(0d);
    }

    /// <summary>
    /// The one that would be found by shipping it. A text box does not mark an
    /// ordinary key press handled — what it acts on is the text input that
    /// follows — so without a check for what has the focus, renaming a module
    /// would play a tune and every letter of the name would be a note nobody
    /// could stop.
    /// </summary>
    [AvaloniaFact]
    public void Typing_a_name_does_not_play_it()
    {
        var (patch, midi) = Board();
        var window = Open(patch);
        var preview = All<PreviewHost>(window).Single();

        Select(window, midi);

        // Double-clicking the panel's heading turns it into a box with the focus
        // in it, which is the only text box a module's own panel offers.
        var title = All<TextBlock>(window).First(t => t.FontSize == 17);
        var at = title.TranslatePoint(
            new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        All<TextBox>(window).FirstOrDefault(t => t.FontSize == 17)
            .ShouldNotBeNull("the heading should have become a box");

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(0d);
    }

    private static double Held(PreviewHost preview, string signal)
    {
        var key = MidiSignal.Key(MidiSources.Keyboard, signal);
        var block = preview.Live;

        return block.At(block.Keys.ToList().IndexOf(key));
    }
}
