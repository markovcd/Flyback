using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Keys;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Keys;

/// <summary>
/// The keys along the foot of the window: what a press on one plays, when the row is
/// up, and how it follows the keyboard's octave and scale.
/// </summary>
public class ScreenKeysTests : EditorTest
{
    /// <summary>A MIDI In whose pitch reaches the picture, so the computer keyboard is its instrument.</summary>
    private static Patch Keyed(KeyboardScale? scale = null)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var midi = b.Add(NodeCatalog.MidiTypeId, 200, 40);

        b.Wire(midi, 0, output, NodeCatalog.OutputColorPort);
        b.Patch.Keyboard = scale;

        return b.Patch;
    }

    /// <summary>A patch nothing plays: the clock alone, on its own.</summary>
    private static Patch Unkeyed()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 700, 40);

        return b.Patch;
    }

    private static Keybed Keybed(MainWindow window) => All<Keybed>(window).Single();

    private static ToggleButton KeysButton(MainWindow window) => All<ToggleButton>(window).Single(button => button.Name == "keys");

    private static Border Key(MainWindow window, string code) => All<Border>(window).Single(border => border.Name == $"key-{code}");

    private static void ShowKeys(MainWindow window)
    {
        KeysButton(window).IsChecked = true;
        Settle(window);
    }

    [AvaloniaFact]
    public void A_key_on_the_screen_plays_its_note_until_it_is_let_go()
    {
        var window = Open(Keyed());
        var preview = All<PreviewHost>(window).Single();

        ShowKeys(window);

        var at = MiddleOf(Key(window, "KeyZ"), window);

        window.MouseDown(at, MouseButton.Left);
        Settle(window);

        Held(preview, MidiSignal.Pitch).ShouldBe(48d);
        Held(preview, MidiSignal.Gate).ShouldBe(1d);

        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(0d);
    }

    [AvaloniaFact]
    public void The_sharps_sit_between_the_white_keys()
    {
        var window = Open(Keyed());

        ShowKeys(window);

        var row = Keybed(window).Row;

        row.Select(key => key.Name).ShouldBe(["C3", "C#3", "D3", "D#3", "E3", "F3", "F#3", "G3", "G#3", "A3", "A#3", "B3", "C4"]);
        row.Where(key => key.Sharp).Select(key => key.Letter).ShouldBe(["S", "D", "G", "H", "J"]);

        var sharp = MiddleOf(Key(window, "KeyS"), window);

        sharp.X.ShouldBeGreaterThan(MiddleOf(Key(window, "KeyZ"), window).X);
        sharp.X.ShouldBeLessThan(MiddleOf(Key(window, "KeyX"), window).X);
        Key(window, "KeyS").Bounds.Height.ShouldBeLessThan(Key(window, "KeyZ").Bounds.Height);
    }

    [AvaloniaFact]
    public void The_octave_buttons_move_the_row()
    {
        var window = Open(Keyed());
        var preview = All<PreviewHost>(window).Single();

        ShowKeys(window);
        Press(All<Button>(window).Single(button => button.Name == "octave-up"));
        Settle(window);

        Keybed(window).Row[0].Name.ShouldBe("C4");

        var at = MiddleOf(Key(window, "KeyZ"), window);

        window.MouseDown(at, MouseButton.Left);
        Settle(window);

        Held(preview, MidiSignal.Pitch).ShouldBe(60d);

        window.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void A_scale_lays_the_home_row_out_from_its_tonic()
    {
        var window = Open(Keyed(new KeyboardScale(2, "dorian")));

        ShowKeys(window);

        var row = Keybed(window).Row;

        row.Select(key => key.Letter).ShouldBe(["A", "S", "D", "F", "G", "H", "J"]);
        row.Select(key => key.Name).ShouldBe(["D3", "E3", "F3", "G3", "A3", "B3", "C4"]);
        row.ShouldAllBe(key => !key.Sharp);
    }

    [AvaloniaFact]
    public void The_keys_are_offered_only_while_a_MIDI_In_listens_to_the_keyboard()
    {
        var window = Open(Unkeyed());

        KeysButton(window).IsVisible.ShouldBeFalse();
        Keybed(window).IsVisible.ShouldBeFalse();

        Editor(window).History.Open(Keyed());
        Settle(window);

        KeysButton(window).IsVisible.ShouldBeTrue();
        Keybed(window).IsVisible.ShouldBeFalse();

        ShowKeys(window);
        Keybed(window).IsVisible.ShouldBeTrue();

        // Put away with the patch that has nothing to play, and back with the next that has.
        Editor(window).History.Open(Unkeyed());
        Settle(window);
        Keybed(window).IsVisible.ShouldBeFalse();
        KeysButton(window).IsVisible.ShouldBeFalse();

        Editor(window).History.Open(Keyed());
        Settle(window);
        Keybed(window).IsVisible.ShouldBeTrue();
    }

    /// <summary>A phone has no keyboard under the hand, so the first finger is the request.</summary>
    [AvaloniaFact]
    public void A_finger_brings_the_keys_up_by_itself()
    {
        var window = Open(Keyed());
        var editor = Editor(window);

        Keybed(window).IsVisible.ShouldBeFalse();

        var at = MiddleOf(editor, window);
        FingerAlong(editor, window, at);

        Keybed(window).IsVisible.ShouldBeTrue();
        KeysButton(window).IsChecked.ShouldBe(true);

        // Let out, the button is the one that decides, finger or not.
        KeysButton(window).IsChecked = false;
        Settle(window);
        FingerAlong(editor, window, at);

        Keybed(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_held_key_is_let_go_when_the_row_is_put_away()
    {
        var window = Open(Keyed());
        var preview = All<PreviewHost>(window).Single();

        ShowKeys(window);

        var at = MiddleOf(Key(window, "KeyZ"), window);

        window.MouseDown(at, MouseButton.Left);
        Settle(window);
        Held(preview, MidiSignal.Gate).ShouldBe(1d);

        KeysButton(window).IsChecked = false;
        Settle(window);

        Held(preview, MidiSignal.Gate).ShouldBe(0d);
    }

    private static double Held(PreviewHost preview, string signal)
    {
        var block = preview.Live;
        var key = block.Keys.FirstOrDefault(candidate =>
            candidate.StartsWith(MidiSources.Keyboard + "/auto/", StringComparison.Ordinal)
            && candidate.EndsWith("/" + signal, StringComparison.Ordinal));

        return key is null ? 0d : block.At(block.Keys.ToList().IndexOf(key));
    }
}
