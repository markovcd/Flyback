using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Gallery;
using Flyback.Editor.Notices;
using Flyback.Plugins.Hosting;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Bars;

/// <summary>
/// The bar along the top: every button on it, what each says, and the order they
/// stand in (ADR-0148). A press raises a notice, and the part that does the thing
/// reacts to it, as it does to the same thing asked for with a key.
/// </summary>
internal sealed class Toolbar : IReactTo<ViewChanged>, IReactTo<TakeMarked>, IReactTo<Touched>
{
    private readonly RecordingState recording;

    /// <summary>What the layout button does to the canvas, which is what it says by default.</summary>
    public const string TidyTip =
        "Lay the modules out so the patch reads left to right  (Ctrl+L). "
        + "Ctrl+click lays out only what is selected, leaving the rest where it is  (Ctrl+Shift+L)";

    /// <summary>What the swap button says while it can be pressed.</summary>
    public const string SwapTip =
        "Swap the preview and the canvas, for a bigger picture while you patch.";

    /// <summary>What it says while it cannot.</summary>
    public const string NoPictureToSwapTip =
        "Nothing is wired into the Output's 'color', so there is no picture to swap in.";

    /// <summary>What the side button does while it can be pressed.</summary>
    public const string SideTip =
        "Show the preview and the inspector beside the canvas, or give the canvas their width.";

    /// <summary>What it says while the canvas is in that column.</summary>
    public const string SideSwappedTip =
        "The canvas is in this column while it is swapped with the preview, so the column stays.";

    /// <summary>What it does in a window too narrow for the canvas and the column side by side.</summary>
    public const string SideNarrowTip =
        "Show the preview and the inspector in the canvas's place, or the canvas again.";

    /// <summary>What the swap button says in such a window.</summary>
    public const string NarrowSwapTip =
        "The window is too narrow to swap the preview and the canvas; the side button shows one or the other.";

    public Button Open { get; } = ToolbarButtons.Drawn("open", Glyphs.Open(), "Open a patch (CTRL+O)…");

    public Button Save { get; } = ToolbarButtons.Drawn("save", Glyphs.Save(), "Save this patch (CTRL+S)…");

    public Button Undo { get; } = ToolbarButtons.Drawn("undo", Glyphs.Undo(), "Take back the last edit  (Ctrl+Z)");

    public Button Redo { get; } = ToolbarButtons.Drawn("redo", Glyphs.Redo(), "Put it back  (Ctrl+Shift+Z)");

    /// <summary>
    /// What laying out means, and whether it is worth doing at all, depends on
    /// which view is showing, so the window rewrites its tip and whether it is on.
    /// </summary>
    public Button Tidy { get; } = ToolbarButtons.Drawn("tidy", Glyphs.Tidy(), TidyTip);

    /// <summary>The module list, for a hand with no right button and no Space bar. Shown once a finger has touched the canvas.</summary>
    public Button Add { get; } = ToolbarButtons.Drawn("add", Glyphs.Add(), "Add a module in the middle of the view  (Space, or hold a finger on the canvas)");

    /// <summary>Framing the whole patch, the other thing a hand has no key for. Shown with <see cref="Add"/>.</summary>
    public Button Frame { get; } = ToolbarButtons.Drawn("frame", Glyphs.Frame(), "Bring the whole patch into view  (Ctrl+F)");

    /// <summary>Plays the patch as it stands in the viewer, in a tab of its own. Only in a page.</summary>
    public Button Viewer { get; } = ToolbarButtons.Drawn("view-it", Glyphs.Viewer(), "View it: play this patch in the viewer, in a tab of its own. The editor pauses behind it.");

    public ToggleButton Code { get; } = ToolbarButtons.Toggle("code", Glyphs.Code(), "Show the patch as text  (F2)");

    public ToggleButton Knobs { get; } =
        ToolbarButtons.Toggle("controls", Glyphs.Knob(), "Show the knob panel, for turning the patch by hand or from a MIDI controller  (Ctrl+K)");

    /// <summary>
    /// Puts the picture in the wide column and the canvas where the picture was.
    /// Enabled only while there is a picture to put there.
    /// </summary>
    public ToggleButton Swap { get; } = ToolbarButtons.Toggle("swap", Glyphs.Swap(), NoPictureToSwapTip);

    /// <summary>Puts the column beside the canvas away and gives the canvas its width. Disabled while swapped.</summary>
    public ToggleButton Side { get; } = ToolbarButtons.Toggle("side", Glyphs.Side(), SideTip);

    /// <summary>Shows the transport row along the foot of the window, or gives its height to the canvas.</summary>
    public ToggleButton Transport { get; } =
        ToolbarButtons.Toggle("transport", Glyphs.Transport(), "Show the row that plays the patch: pause, the seek bar, its length, loop and Volume.");

    /// <summary>Starts and stops a take (ADR-0080). What its tip says is decided per patch by TakeRecording.Mark. Only outside a page.</summary>
    public Button Record { get; } = ToolbarButtons.Drawn("record", Glyphs.Record(), TakeRecording.RecordTip);

    /// <summary>Runs the patch for a few seconds from the playhead and pins what each output carries. Only outside a page.</summary>
    public Button Measure { get; } = ToolbarButtons.Drawn("measure", Glyphs.Measure(), MeasureTip);

    public const string MeasureTip =
        "Measure: run the patch for a few seconds from the playhead and pin what each output carries beside it, "
        + "the selected modules' or every module's. Again hides them, or measures afresh after an edit  (Ctrl+M)";

    public ToggleButton Assistant { get; } =
        ToolbarButtons.Toggle("assistant", Glyphs.Spark(), "Describe a patch and have one built.");

    public Button Settings { get; } = ToolbarButtons.Drawn("settings", Glyphs.Settings(), "Open the settings.");

    public Button Plugins { get; } = ToolbarButtons.Drawn("plugins", Glyphs.Plug(), "Find, install and update plugins.");

    public Button About { get; } = ToolbarButtons.Drawn("about", Glyphs.About(), "What this is, who wrote it, and what it may be done with.");

    /// <summary>The Flyback mark, first on the bar, back to the site's front page. Only where the host has one.</summary>
    public Button Home { get; } = ToolbarButtons.Drawn("home", new LogoMark { Width = 30, Height = 30 }, "Back to the Flyback site.");

    /// <summary>The bar itself.</summary>
    public Control View { get; }

    /// <param name="presets">The preset slot, first on the bar.</param>
    /// <param name="plugins">Whether any assistant plugin is installed.</param>
    /// <param name="recording">Whether a take is running, which no other patch may be opened under.</param>
    /// <param name="host">Whether the editor is in a page, whose bar has none of what a page cannot do.</param>
    public Toolbar(PresetSlot presets, PluginCatalog plugins, Reactions reactions, IDialog dialog, RecordingState recording, EditorHost host)
    {
        var full = !host.InPage;

        this.recording = recording;

        Open.Click += (_, _) => reactions.Raise(new OpenAsked());
        Save.Click += (_, _) => reactions.Raise(new SaveAsked());
        Undo.Click += (_, _) => reactions.Raise(new UndoAsked());
        Redo.Click += (_, _) => reactions.Raise(new RedoAsked());
        Add.Click += (_, _) => reactions.Raise(new ModuleAsked());
        Frame.Click += (_, _) => reactions.Raise(new FrameAsked());
        Viewer.Click += (_, _) => reactions.Raise(new ViewAsked());
        Code.IsCheckedChanged += (_, _) => reactions.Raise(new CodeAsked(Code.IsChecked == true));
        Knobs.IsCheckedChanged += (_, _) => reactions.Raise(new KnobsAsked(Knobs.IsChecked == true));
        Side.IsChecked = true;
        Side.IsCheckedChanged += (_, _) => reactions.Raise(new SideAsked(Side.IsChecked == true));
        Swap.IsCheckedChanged += (_, _) => reactions.Raise(new SwapAsked(Swap.IsChecked == true));
        Transport.IsChecked = true;
        Transport.IsCheckedChanged += (_, _) => reactions.Raise(new TransportAsked(Transport.IsChecked == true));
        Assistant.IsCheckedChanged += (_, _) => reactions.Raise(new AssistantAsked(Assistant.IsChecked == true));
        Record.Click += (_, _) => reactions.Raise(new RecordAsked());
        Measure.Click += (_, _) => reactions.Raise(new MeasureAsked());
        Settings.Click += (_, _) => reactions.Raise(new SettingsAsked());
        Plugins.Click += (_, _) => reactions.Raise(new PluginsAsked());
        About.Click += async (_, _) => await dialog.Show("About", Controls.About.View());
        Home.Click += (_, _) => host.Home?.Invoke();

        var assistants = plugins.Assistants.Count > 0;

        // A locked canvas says why in its tip, and that is wasted unless a
        // disabled button is still allowed to show it. The same for a patch with
        // no picture to swap in, and for a side column the canvas is standing in.
        ToolTip.SetShowOnDisabled(Tidy, true);
        ToolTip.SetShowOnDisabled(Swap, true);
        ToolTip.SetShowOnDisabled(Side, true);
        ToolTip.SetShowOnDisabled(Record, true);

        // A Click says which button was pressed and nothing about what was held
        // down while it was, so that is read on the way in. The key offers both
        // layouts and so does the button, because a modifier is how a toolbar
        // offers the narrower of two things without a second glyph for it.
        var modifiers = KeyModifiers.None;

        Tidy.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => modifiers = e.KeyModifiers,
            RoutingStrategies.Tunnel);

        Tidy.Click += (_, _) =>
        {
            reactions.Raise(new TidyAsked(OnlySelected: (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0));
            modifiers = KeyModifiers.None;
        };

        Assistant.IsEnabled = assistants;
        ToolTip.SetTip(Assistant, assistants
            ? "Describe a patch and have one built. Nothing is sent until you ask, and what "
              + "comes back is an edit Ctrl+Z takes off again."
            : "No assistant plugin is installed. See About for where plugins are looked for.");

        // What is done to the patch, in the order it is done: pick one, open or
        // save one, take an edit back. Tidy sits with undo and redo rather than
        // with the files, because it is an edit and is taken back like one.
        var patchwork = ToolbarButtons.Group();

        if (host.Home is not null) patchwork.Children.Add(Home);
        patchwork.Children.Add(presets.View);
        if (!full) patchwork.Children.Add(Viewer);

        if (full)
        {
            patchwork.Children.Add(Open);
            patchwork.Children.Add(Save);
        }

        // First among the edits, where a hand looks for them; a mouse has the canvas for both.
        Add.IsVisible = Frame.IsVisible = false;

        patchwork.Children.Add(ToolbarButtons.Separator());
        patchwork.Children.Add(Add);
        patchwork.Children.Add(Frame);
        patchwork.Children.Add(Undo);
        patchwork.Children.Add(Redo);
        patchwork.Children.Add(Tidy);
        patchwork.Children.Add(ToolbarButtons.Separator());
        if (full) patchwork.Children.Add(Assistant);
        patchwork.Children.Add(Code);
        patchwork.Children.Add(Knobs);
        patchwork.Children.Add(Swap);
        patchwork.Children.Add(Side);
        patchwork.Children.Add(Transport);

        if (full)
        {
            patchwork.Children.Add(ToolbarButtons.Separator());
            patchwork.Children.Add(Measure);
            patchwork.Children.Add(Record);
        }

        // The other end of the bar, because none of these is about the patch:
        // they are the program itself, and a thing reached for once a session
        // does not belong in the path of the things reached for constantly.
        var program = ToolbarButtons.Group();

        program.Children.Add(Settings);
        program.Children.Add(Plugins);
        program.Children.Add(About);

        // Left to right, rather than the program group docked to the far edge —
        // everything reached from the toolbar sits together at the near side instead
        // of one end chasing the window's width. One row however narrow the window:
        // what does not fit folds into the menu at the end. Unmargined itself: each
        // group carries its own margin already, from ToolbarButtons.Group(), and a
        // second one here would double the gaps.
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var programRule = ToolbarButtons.Separator();

        bar.Children.Add(patchwork);

        if (full)
        {
            bar.Children.Add(programRule);
            bar.Children.Add(program);
        }

        // The first to fold is the first listed: the program's own buttons, reached
        // for once a session, before the files, and the views last.
        Overflow = new ToolbarOverflow(
            bar,
            [
                (About, "About"),
                (Plugins, "Plugins"),
                (Settings, "Settings"),
                (Swap, "Swap the preview and the canvas"),
                (Assistant, "Assistant"),
                (Tidy, "Lay out the modules"),
                (Save, "Save…"),
                (Open, "Open…"),
                (Viewer, "View it"),
                (Code, "Show the patch as text"),
                (Knobs, "Knob panel"),
                (Transport, "Transport row"),
                (Redo, "Redo"),
                (Measure, "Measure"),
                (Record, "Record"),
                (Frame, "Bring the whole patch into view"),
            ],
            full ? [(program, programRule)] : []);

        bar.Children.Add(Overflow.More);

        View = new Border
        {
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 0, 0, 1),
            ClipToBounds = true,
            Child = bar,
        };

        View.SizeChanged += (_, e) => Overflow.Fit(e.NewSize.Width);
    }

    /// <summary>The menu what does not fit on the bar folds into.</summary>
    public ToolbarOverflow Overflow { get; }

    /// <summary>The code button follows the view, however the view was switched.</summary>
    public Task On(ViewChanged notice)
    {
        if (Code.IsChecked != notice.ShowingCode) Code.IsChecked = notice.ShowingCode;

        return Task.CompletedTask;
    }

    public Task On(Touched notice)
    {
        Add.IsVisible = Frame.IsVisible = true;
        Overflow.Fit(View.Bounds.Width);
        return Task.CompletedTask;
    }

    public Task On(TakeMarked notice)
    {
        Open.IsEnabled = !recording.Running;
        return Task.CompletedTask;
    }
}
