using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Gallery;
using Flyback.Plugins.Hosting;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Bars;

/// <summary>
/// The bar along the top: every button on it, what each says, and the order they
/// stand in (ADR-0148). What a press does is the window's, which wires each one.
/// </summary>
internal sealed class Toolbar
{
    /// <summary>What the layout button does to the canvas, which is what it says by default.</summary>
    public const string TidyTip =
        "Lay the modules out so the patch reads left to right  (Ctrl+L). "
        + "Ctrl+click lays out only what is selected, leaving the rest where it is  (Ctrl+Shift+L)";

    public const string PauseTip = "Pause the patch, in the picture and in the sound.  (Ctrl+P)";

    public const string PlayTip = "Play the patch on from where it stopped.  (Ctrl+P)";

    /// <summary>What the rewind button does.</summary>
    public const string RewindTip =
        "Take the patch back to zero seconds, in the picture and in the sound.";

    /// <summary>What the swap button says while it can be pressed.</summary>
    public const string SwapTip =
        "Swap the preview and the canvas, for a bigger picture while you patch.";

    /// <summary>What it says while it cannot.</summary>
    public const string NoPictureToSwapTip =
        "Nothing is wired into the Output's 'color', so there is no picture to swap in.";

    public Button Open { get; } = ToolbarButtons.Drawn("open", Glyphs.Open(), "Open a patch (CTRL+O)…");

    public Button Save { get; } = ToolbarButtons.Drawn("save", Glyphs.Save(), "Save this patch (CTRL+S)…");

    public Button Undo { get; } = ToolbarButtons.Glyph("undo", "↶", "Take back the last edit  (Ctrl+Z)");

    public Button Redo { get; } = ToolbarButtons.Glyph("redo", "↷", "Put it back  (Ctrl+Shift+Z)");

    /// <summary>
    /// What laying out means, and whether it is worth doing at all, depends on
    /// which view is showing, so the window rewrites its tip and whether it is on.
    /// </summary>
    public Button Tidy { get; } = ToolbarButtons.Drawn("tidy", Glyphs.Tidy(), TidyTip);

    public ToggleButton Code { get; } = ToolbarButtons.Toggle("code", "{ }", "Show the patch as text  (F2)");

    public ToggleButton Knobs { get; } =
        ToolbarButtons.Toggle("controls", "◎", "Show the knob panel, for turning the patch by hand or from a MIDI controller  (Ctrl+K)");

    /// <summary>
    /// Puts the picture in the wide column and the canvas where the picture was.
    /// Enabled only while there is a picture to put there.
    /// </summary>
    public ToggleButton Swap { get; } = ToolbarButtons.Toggle("swap", "⇄", NoPictureToSwapTip);

    public Button Pause { get; } = new();

    /// <summary>
    /// Takes the patch back to zero seconds, in the picture and in the sound.
    /// Beside Record rather than on the Output's panel — ADR-0081, the same move
    /// ADR-0080 made for Record.
    /// </summary>
    public Button Rewind { get; } = new();

    /// <summary>The patch's clock, to drag anywhere along a length the user sets.</summary>
    public SeekBar Seek { get; }

    /// <summary>
    /// Starts and stops a take (ADR-0080). Its glyph swaps between the dot and the
    /// square rather than its label, since a toolbar button here carries no text.
    /// </summary>
    public Button Record { get; } = new();

    public ToggleButton Assistant { get; } =
        ToolbarButtons.Toggle("assistant", "✦", "Describe a patch and have one built.");

    public Button Settings { get; } = ToolbarButtons.Glyph("settings", "⚙", "Open the settings.");

    public Button Plugins { get; } = ToolbarButtons.Drawn("plugins", Glyphs.Plug(), "Find, install and update plugins.");

    public Button About { get; } = ToolbarButtons.Glyph("about", "ⓘ", "What this is, who wrote it, and what it may be done with.");

    /// <summary>The bar itself.</summary>
    public Control View { get; }

    /// <summary>Tidy was pressed; true where Ctrl was held, which lays out only what is selected.</summary>
    public event EventHandler<bool>? Tidied;

    /// <param name="presets">The preset slot, first on the bar.</param>
    /// <param name="plugins">Whether any assistant plugin is installed.</param>
    public Toolbar(PresetSlot presets, PluginCatalog plugins, SeekBar seek)
    {
        Seek = seek;

        var assistants = plugins.Assistants.Count > 0;

        // A locked canvas says why in its tip, and that is wasted unless a
        // disabled button is still allowed to show it. The same for a patch with
        // no picture to swap in, and for Record grayed out during a take.
        ToolTip.SetShowOnDisabled(Tidy, true);
        ToolTip.SetShowOnDisabled(Swap, true);
        ToolTip.SetShowOnDisabled(Record, true);

        // A Click says which button was pressed and nothing about what was held
        // down while it was, so that is read on the way in. The key offers both
        // layouts and so does the button, because a modifier is how a toolbar
        // offers the narrower of two things without a second glyph for it.
        var modifiers = KeyModifiers.None;

        Tidy.AddHandler(
            InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => modifiers = e.KeyModifiers,
            RoutingStrategies.Tunnel);

        Tidy.Click += (_, _) =>
        {
            Tidied?.Invoke(this, (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0);
            modifiers = KeyModifiers.None;
        };

        // What the record tip actually says is decided per patch by
        // TakeRecording.Mark, which runs before this is ever shown.
        ToolbarButtons.Marked(Record, "record", Glyphs.Record(), TakeRecording.RecordTip);
        ToolbarButtons.Marked(Pause, "pause", Glyphs.Pause(), PauseTip);
        ToolbarButtons.Marked(Rewind, "rewind", Glyphs.Rewind(), RewindTip);

        Assistant.IsEnabled = assistants;
        ToolTip.SetTip(Assistant, assistants
            ? "Describe a patch and have one built. Nothing is sent until you ask, and what "
              + "comes back is an edit Ctrl+Z takes off again."
            : "No assistant plugin is installed. See About for where plugins are looked for.");

        // What is done to the patch, in the order it is done: pick one, open or
        // save one, take an edit back. Tidy sits with undo and redo rather than
        // with the files, because it is an edit and is taken back like one.
        var patchwork = ToolbarButtons.Group();

        patchwork.Children.Add(presets.View);
        patchwork.Children.Add(Open);
        patchwork.Children.Add(Save);
        patchwork.Children.Add(ToolbarButtons.Separator());
        patchwork.Children.Add(Undo);
        patchwork.Children.Add(Redo);
        patchwork.Children.Add(Tidy);
        patchwork.Children.Add(ToolbarButtons.Separator());
        patchwork.Children.Add(Code);
        patchwork.Children.Add(Knobs);
        patchwork.Children.Add(Swap);

        // On its own, between what is done to the patch and what is done to
        // the program: pausing, rewinding, seeking and recording are neither — all are
        // facts about the performance, not an edit Ctrl+Z takes back.
        var transport = ToolbarButtons.Group();
        transport.Children.Add(Pause);
        transport.Children.Add(Rewind);
        transport.Children.Add(Seek.View);
        transport.Children.Add(Record);

        // The other end of the bar, because none of these is about the patch:
        // they are the program itself, and a thing reached for once a session
        // does not belong in the path of the things reached for constantly.
        var program = ToolbarButtons.Group();

        program.Children.Add(Assistant);
        program.Children.Add(Settings);
        program.Children.Add(Plugins);
        program.Children.Add(About);

        // One row, left to right, rather than the program group docked to the
        // far edge — everything reached from the toolbar sits together at the
        // near side instead of one end chasing the window's width. Unmargined
        // itself: each group carries its own margin already, from
        // ToolbarButtons.Group(), and a second one here would double the gaps.
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        bar.Children.Add(patchwork);
        bar.Children.Add(ToolbarButtons.Separator());
        bar.Children.Add(transport);
        bar.Children.Add(ToolbarButtons.Separator());
        bar.Children.Add(program);

        View = new Border
        {
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }
}
