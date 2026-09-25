using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Inspect;
using Flyback.Core.Graph;

namespace Flyback.Specs.Support;

/// <summary>
/// The editor as somebody has it open: its window, built by the editor's own
/// container (ADR-0150), holding the scenario's patch and answering the keys a
/// person presses. Headless, so there is no screen and nothing to click by hand.
/// </summary>
/// <remarks>
/// Opened the first time a step asks for it, on the patch the scenario has built
/// by then, and closed with the scenario. Every step that touches it runs on the
/// one UI thread the session keeps, and hands the patch the editor now holds back
/// to <see cref="PatchContext"/>, so the screen and the speakers are checked
/// against what is on the canvas. Scenarios that open it take their
/// <see cref="HeadlessTurn"/>, so in a parallel run their durations are mostly
/// waiting: one takes a tenth of a second or so on its own, after the first window's two.
/// </remarks>
public sealed class Editor(PatchContext context, HeadlessTurn turn) : IDisposable
{
    /// <summary>How many turns the UI thread is given after a step, enough for a clipboard's round trip.</summary>
    private const int Turns = 4;

    private MainWindow? window;

    private readonly List<string> said = [];

    /// <summary>Everything the canvas has said since the window opened.</summary>
    public IReadOnlyList<string> Said => said;

    /// <summary>What is selected on the canvas, as the editor holds it now.</summary>
    internal IReadOnlyList<NodeInstance> Selected => Read(canvas => canvas.Selection.Nodes);

    /// <summary>The window's title: the document's name, then the program's.</summary>
    public string Title => ReadWindow(open => open.Title ?? string.Empty);

    /// <summary>Whether the title says there is work to lose.</summary>
    public bool Unsaved => Read(_ => window!.Title?.EndsWith('•') == true);

    /// <summary>Whether the toolbar's undo has anything to take back.</summary>
    public bool CanUndo => Read(canvas => canvas.History.CanUndo);

    /// <summary>
    /// Where the editor keeps and looks for unsaved work, and whether it opens on the
    /// scenario's patch. Said before anything opens it.
    /// </summary>
    public EditorSetup Setup { get; set; } = new();

    /// <summary>Whether the window opens on the scenario's patch, rather than on whatever it starts with itself.</summary>
    public bool OnThePatch { get; set; } = true;

    /// <summary>Opens the window, if it is not open already.</summary>
    public void Open() => Do(_ => { });

    /// <summary>The question up over the window, as its words, or null while there is none.</summary>
    public string? Asking => ReadWindow(open =>
        open.GetVisualDescendants().OfType<ModalOverlay>().FirstOrDefault() is { } dialog
            ? string.Join(" ", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            : null);

    /// <summary>The preset the toolbar says is on the canvas, or null for a document that is none.</summary>
    public string? Showing => ReadWindow(open => (Presets(open).SelectedItem as PatchPreset)?.Name);

    /// <summary>Everything the window's report line has said.</summary>
    public IReadOnlyList<string> Reported => ReadWindow(open => open.GetVisualDescendants().OfType<ReportLine>().Single().History);

    /// <summary>Picks a preset from the toolbar's list, which is where the gallery's tiles land too.</summary>
    public void PickPreset(string name) =>
        DoWindow((open, _) =>
        {
            var presets = Presets(open);
            var row = presets.ItemsSource!.Cast<object>().ToList().FindIndex(item => item is PatchPreset preset && preset.Name == name);

            presets.SelectedIndex = row >= 0 ? row : throw new InvalidOperationException($"No preset called {name}.");
        });

    /// <summary>Answers the question up over the window with the button so labeled.</summary>
    public void Answer(string label) =>
        DoWindow((open, _) =>
        {
            var dialog = open.GetVisualDescendants().OfType<ModalOverlay>().Single();

            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == label)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

    /// <summary>Drags a wire out of a module's output and lets it go over bare canvas, low on the left.</summary>
    public void DropWireFrom(Guid node, int output) =>
        DoWindow((open, canvas) =>
        {
            var from = canvas.TranslatePoint(
                canvas.GraphToScreen.Transform(NodeGeometry.OutputPort(canvas.History.Patch.Find(node)!, output)), open)!.Value;
            var bare = canvas.TranslatePoint(new Point(40, canvas.Bounds.Height - 40), open)!.Value;

            open.MouseDown(from, MouseButton.Left);
            open.MouseMove(bare);
            open.MouseUp(bare, MouseButton.Left);
        });

    /// <summary>Picks a module by name from the list that is open.</summary>
    public void PickFromList(string name) =>
        DoWindow((open, _) =>
        {
            var list = open.GetVisualDescendants().OfType<ModulePalette>().FirstOrDefault()
                ?? throw new InvalidOperationException("no list of modules is open");

            var entry = list.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == name);

            entry.Focus();
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

    /// <summary>
    /// What the module panel says beside the row captioned <paramref name="caption"/>,
    /// or null where it has no such row.
    /// </summary>
    public string? PanelRow(string caption) =>
        ReadWindow(open => Row(open, caption) is var (label, row)
            ? string.Join(" ", row.GetVisualDescendants().OfType<TextBlock>().Where(t => t != label).Select(t => t.Text))
            : null);

    /// <summary>Whether the row captioned <paramref name="caption"/> has a button so named.</summary>
    public bool RowOffers(string caption, string button) =>
        ReadWindow(open => Row(open, caption) is var (_, row) && RowButton(row, button) is not null);

    /// <summary>Presses the button so named on the row captioned <paramref name="caption"/>.</summary>
    public void PressInRow(string caption, string button) =>
        DoWindow((open, _) =>
        {
            var (_, row) = Row(open, caption) ?? throw new InvalidOperationException($"the panel has no row '{caption}'");

            (RowButton(row, button) ?? throw new InvalidOperationException($"the row '{caption}' has no {button}"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

    /// <summary>Renames the one module selected: a double-click on its name on the panel, then typing and Enter.</summary>
    /// <remarks>One turn of the thread from click to Enter, so no other scenario's window takes the focus in between.</remarks>
    public void Rename(string name) =>
        DoWindow((open, _) =>
        {
            var title = open.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "moduleName");
            var at = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), open)!.Value;

            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
            Settle();

            var box = open.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Classes.Contains(ModulePlate.NameBoxClass))
                ?? throw new InvalidOperationException("the name did not become a box to type into");

            box.Focus();
            box.Text = name;
            open.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            open.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        });

    /// <summary>Opens the settings from the toolbar, on the tab so named.</summary>
    public void OpenSettings(string tab) =>
        DoWindow((open, _) =>
        {
            open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "settings")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The dialog is put up on a later turn than the click.
            for (var turn = 0; turn < 20 && !open.GetVisualDescendants().OfType<ModalOverlay>().Any(); turn++)
                Dispatcher.UIThread.RunJobs();

            Settle();

            var tabs = open.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "settingsTabs");

            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => (item.Header as TextBlock)?.Text == tab);
        });

    /// <summary>Ticks or clears the box so labeled on the question up over the window.</summary>
    public void Tick(string label, bool on) =>
        DoWindow((open, _) =>
            open.GetVisualDescendants().OfType<ModalOverlay>().Single()
                .GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == label)
                .IsChecked = on);

    /// <summary>Rests the pointer over a point on the canvas, in the patch's own coordinates.</summary>
    internal void Hover(Func<NodeEditor, Point> graph) =>
        DoWindow((open, canvas) =>
            open.MouseMove(canvas.TranslatePoint(canvas.GraphToScreen.Transform(graph(canvas)), open)!.Value));

    /// <summary>What the canvas's tooltip says, or null while it says nothing.</summary>
    public string? Tip => Read(canvas => ToolTip.GetTip(canvas) as string);

    /// <summary>Clicks the seek bar where <paramref name="seconds"/> falls along it.</summary>
    public void Seek(double seconds) =>
        DoWindow((open, _) =>
        {
            var bar = SeekBar(open);
            var at = bar.TranslatePoint(bar.At(seconds), open)!.Value;

            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
        });

    /// <summary>Switches the seek bar's loop on, as a click on it does.</summary>
    public void LoopSeekBar() =>
        DoWindow((open, _) =>
        {
            var loop = open.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "seekLoop");
            var at = loop.TranslatePoint(new Point(loop.Bounds.Width / 2, loop.Bounds.Height / 2), open)!.Value;

            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
        });

    /// <summary>
    /// Waits for the patch's clock to come to <paramref name="arrived"/>, for at most
    /// <paramref name="cap"/>, and says whether it did.
    /// </summary>
    /// <remarks>
    /// The clock moves on the UI thread's timers, and headless fires those only while a
    /// dispatch is waiting on the thread, so each look waits there rather than beside it.
    /// </remarks>
    public bool WaitForClock(Func<double, bool> arrived, TimeSpan cap)
    {
        var until = DateTime.UtcNow + cap;

        while (!arrived(Clock))
        {
            if (DateTime.UtcNow > until) return false;

            Run(async () =>
            {
                await Task.Delay(20);
                return true;
            });
        }

        return true;
    }

    /// <summary>Waits for the patch to stop by itself, for at most <paramref name="cap"/>, and says whether it did.</summary>
    public bool WaitForStop(TimeSpan cap)
    {
        var until = DateTime.UtcNow + cap;

        while (!Paused)
        {
            if (DateTime.UtcNow > until) return false;

            Run(async () =>
            {
                await Task.Delay(20);
                return true;
            });
        }

        return true;
    }

    /// <summary>Whether a seek bar waits behind its dots at the top of the full-screen picture.</summary>
    public bool SeekBarAtTheTop => ReadWindow(open =>
        open.GetVisualDescendants().OfType<SeekOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } seek
        && seek.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Top);

    /// <summary>Types a length into the box beside the seek bar, and presses Enter.</summary>
    public void SetSeekLength(string typed) =>
        DoWindow((open, _) =>
        {
            var box = open.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "seekLength");

            box.Focus();
            box.Text = typed;
            open.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            open.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        });

    /// <summary>How many seconds the seek bar spans.</summary>
    public double SeekLength => ReadWindow(open => SeekBar(open).Maximum);

    /// <summary>Where the patch's clock is, in seconds: what the status bar says after "t =".</summary>
    public double Clock => ReadWindow(open => open.GetVisualDescendants().OfType<PreviewHost>().First().Time);

    /// <summary>Whether the toolbar offers to play rather than to pause.</summary>
    public bool Paused => ReadWindow(open => ToolTip.GetTip(open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "pause")) as string == Toolbar.PlayTip);

    /// <summary>Gives the picture the whole window, as a double-click on it does.</summary>
    public void FullScreen() =>
        DoWindow((open, _) =>
        {
            var preview = open.GetVisualDescendants().OfType<PreviewHost>().Single();
            var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), open)!.Value;

            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
        });

    /// <summary>What the line in the full-screen picture's corner says, or null while it is not showing.</summary>
    public string? Stats => ReadWindow(open =>
        open.GetVisualDescendants().OfType<StatsOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } stats
            ? stats.Said
            : null);

    /// <summary>Makes the selection exactly these modules, which is what clicking them with Ctrl held does.</summary>
    public void Select(params Guid[] ids) =>
        Do(canvas =>
        {
            canvas.Selection.Take(ids);
            canvas.Selection.Announce();
        });

    /// <summary>Presses a key, with Ctrl or anything else held, while the canvas has the focus.</summary>
    public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        Do(canvas =>
        {
            var open = window!;

            // The keyboard is the whole program's, so the window takes it back first, onto
            // the canvas or, where that is put away, onto whatever the window keeps it on.
            open.Activate();

            if (!canvas.Focus()) (open.FocusManager?.GetFocusedElement() as InputElement)?.Focus();

            open.KeyPressQwerty(key, modifiers);
            open.KeyReleaseQwerty(key, modifiers);
        });

    /// <summary>Presses a key with Ctrl held, as every editing shortcut is.</summary>
    public void PressCtrl(PhysicalKey key, bool shift = false) =>
        Press(key, RawInputModifiers.Control | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));

    /// <summary>Runs what a step does to the canvas, where no key does it: a panel's button, say.</summary>
    internal void Do(Action<NodeEditor> act) => DoWindow((_, canvas) => act(canvas));

    /// <summary>Runs what a step does to the window, and gives the thread its turns to answer.</summary>
    internal void DoWindow(Action<MainWindow, NodeEditor> act) =>
        Run(async () =>
        {
            act(Window(), Canvas());

            // A clipboard answers asynchronously, off this thread and back onto it,
            // so the thread is given real turns rather than only its queue emptied.
            for (var turn = 0; turn < Turns; turn++)
            {
                Settle();
                await Task.Delay(1);
            }

            context.Replace(Canvas().History.Patch);
            return true;
        });

    internal T Read<T>(Func<NodeEditor, T> read) => Run(() => read(Canvas()));

    private T ReadWindow<T>(Func<MainWindow, T> read) => Run(() => read(Window()));

    public void Dispose()
    {
        try
        {
            if (window is not { } open) return;

            window = null;

            Run(() =>
            {
                open.CloseWithoutAsking();
                Dispatcher.UIThread.RunJobs();
            });
        }
        finally
        {
            turn.Leave(this);
        }
    }

    /// <summary>The window, opened the first time, on the scenario's patch where it is to be.</summary>
    private MainWindow Window()
    {
        if (window is not null) return window;

        window = EditorServices.Window(Setup);
        window.Show();
        Settle();

        var canvas = CanvasIn(window);

        canvas.Report.Said += (_, line) => said.Add(line);
        // As a file arrives: the canvas holds it, and no preset is said to be showing.
        if (OnThePatch)
        {
            window.ClearPresetSelection();
            canvas.History.Open(context.Patch);
        }

        canvas.Focus();
        Settle();

        return window;
    }

    private NodeEditor Canvas() => CanvasIn(Window());

    /// <summary>
    /// The caption on the module panel reading <paramref name="caption"/>, and the whole
    /// row it heads: everything up to the panel itself.
    /// </summary>
    private static (TextBlock Caption, Control Row)? Row(MainWindow window, string caption)
    {
        var panel = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "inspector");

        if (panel.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == caption) is not { } label)
            return null;

        Control row = label;
        while (row.GetVisualParent() is Control parent && parent != panel) row = parent;

        return (label, row);
    }

    private static Button? RowButton(Control row, string name) =>
        row.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == name);

    private static SeekTrack SeekBar(MainWindow window) =>
        window.GetVisualDescendants().OfType<SeekTrack>().Single(track => track.Name == "seek");

    private static ComboBox Presets(MainWindow window) =>
        window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "presets");

    private static NodeEditor CanvasIn(MainWindow window) =>
        window.GetVisualDescendants().OfType<NodeEditor>().Single();

    private void Settle()
    {
        window!.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private void Run(Action act)
    {
        turn.Take(this);
        Headless.Run(act);
    }

    private T Run<T>(Func<T> act)
    {
        turn.Take(this);
        return Headless.Run(act);
    }

    private T Run<T>(Func<Task<T>> act)
    {
        turn.Take(this);
        return Headless.Run(act);
    }
}
