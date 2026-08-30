using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The lists in the window are pointed at, not typed at.
/// </summary>
/// <remarks>
/// A ComboBox answers the keyboard twice over and commits on every step: an arrow
/// moves the selection with the dropdown shut, and a letter jumps to the first
/// item beginning with it. On the preset list that is a patch thrown away per
/// keystroke, and on any of them it collides with the letters being an instrument
/// — so a Picker reads neither.
/// <para>
/// Both routes are covered separately because they are separate: the arrow
/// arrives as a key press and the letter as text input, and a fix for one is no
/// fix at all for the other. That is how this was found.
/// </para>
/// </remarks>
public class PickerTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>The toolbar's list of patches to start from.</summary>
    private static ComboBox Presets(MainWindow window) => All<ComboBox>(window)
        .First(box => box.ItemsSource?.Cast<object>().Any(item => item as string == "Plasma") == true);

    /// <summary>
    /// It still looks like a list.
    /// </summary>
    /// <remarks>
    /// The one this class got wrong, and the one every other test here was blind
    /// to. A theme is found by type, and a control that does not say to look for
    /// its base type's is given no template — so the list went on holding its
    /// items, raising SelectionChanged and answering every question correctly,
    /// while drawing nothing at all. Every test above passed against a control
    /// nobody could see.
    /// <para>
    /// Checked as "it has a template and takes up room", which is what an
    /// untemplated control has and is neither of: its visual tree is itself alone
    /// and it measures to nothing.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_list_is_drawn_like_a_list()
    {
        var window = Open();
        var presets = Presets(window);

        // Against the plain one beside it rather than against a number: what
        // matters is that a Picker is as visible as the ComboBox it stands in
        // for, and the theme decides how tall that is.
        var plain = new ComboBox { ItemsSource = new[] { "one", "two" }, SelectedIndex = 0 };

        Show(plain);

        presets.ShouldBeOfType<Picker>();
        Tree(presets).Count().ShouldBeGreaterThan(1, "an untemplated control is its own whole tree");
        presets.Bounds.Height.ShouldBe(plain.Bounds.Height, 1d);
        presets.Bounds.Width.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void A_letter_typed_at_the_patch_list_does_not_switch_the_patch()
    {
        var window = Open();
        var presets = Presets(window);

        presets.Focus();
        Settle(window);

        // 'k' is what took it to Kaleidoscope, one item down, before this existed.
        window.KeyTextInput("k");
        Settle(window);

        presets.SelectedIndex.ShouldBe(0);
    }

    [AvaloniaFact]
    public void An_arrow_at_the_patch_list_does_not_switch_the_patch()
    {
        var window = Open();
        var presets = Presets(window);

        presets.Focus();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Settle(window);

        presets.SelectedIndex.ShouldBe(0);
    }

    /// <summary>
    /// And the patch itself is left alone, which is the thing that was actually
    /// wrong: the selection moving is only how it showed.
    /// </summary>
    [AvaloniaFact]
    public void The_patch_on_the_canvas_survives_a_keystroke_at_the_list()
    {
        var window = Open();
        var editor = All<NodeEditor>(window).Single();
        var presets = Presets(window);

        var before = editor.Patch.Nodes.Select(node => node.Id).ToList();

        presets.Focus();
        Settle(window);

        window.KeyTextInput("k");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Settle(window);

        editor.Patch.Nodes.Select(node => node.Id).ShouldBe(before);
    }

    /// <summary>
    /// Ignored rather than swallowed. The picker declines to read the key and
    /// does not claim it, so what a letter reaches is whatever it would have
    /// reached with nothing focused — the instrument, when the patch holds a
    /// MIDI In.
    /// </summary>
    [AvaloniaFact]
    public void A_letter_at_a_focused_list_still_plays()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var midi = b.Add(NodeCatalog.MidiTypeId, 200, 40);
        b.Wire(midi, 0, output, NodeCatalog.OutputColorPort);

        var window = Open();

        All<NodeEditor>(window).Single().Patch = b.Patch;
        Settle(window);

        Presets(window).Focus();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Settle(window);

        var preview = All<PreviewHost>(window).Single();
        var gate = preview.Live.Keys.First(key =>
            key.StartsWith(MidiSources.Keyboard + "/auto/", StringComparison.Ordinal)
            && key.EndsWith("/" + MidiSignal.Gate, StringComparison.Ordinal));

        preview.Live.At(preview.Live.Keys.ToList().IndexOf(gate)).ShouldBe(1d);
    }

    /// <summary>
    /// The preview size and the instrument a MIDI In listens to are the same kind
    /// of list and have the same trouble. Neither throws a patch away, so neither
    /// is as bad as the preset list — but the size list is all digits, and the
    /// digits are notes as surely as the letters are.
    /// </summary>
    /// <remarks>
    /// Both are reached by selecting the module whose panel holds them, because
    /// neither is in the tree until then. Asked of the type rather than by
    /// pressing keys at them: what a Picker does with a keystroke is the two
    /// tests above, and repeating them per list would be testing the same class
    /// three times.
    /// </remarks>
    [AvaloniaFact]
    public void The_other_lists_in_the_window_are_pickers_as_well()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var midi = b.Add(NodeCatalog.MidiTypeId, 200, 40);
        b.Wire(midi, 0, output, NodeCatalog.OutputColorPort);

        var window = Open();

        All<NodeEditor>(window).Single().Patch = b.Patch;
        Settle(window);

        // The Output's panel, which is where the preview size lives.
        Select(window, output);

        Named(window, "960 x 540").ShouldBeOfType<Picker>();

        // And the MIDI In's, which is where the instrument is picked.
        Select(window, midi);

        All<ComboBox>(window)
            .First(box => box.ItemsSource?.Cast<object>().Any(item => item is ChoiceOption) == true)
            .ShouldBeOfType<Picker>();
    }

    /// <summary>Clicks a module's header, which is what puts its panel up.</summary>
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

    /// <summary>The list holding a given entry, whatever panel it is in.</summary>
    private static ComboBox Named(MainWindow window, string entry) => All<ComboBox>(window)
        .First(box => box.ItemsSource?.Cast<object>().Any(item => item as string == entry) == true);
}
