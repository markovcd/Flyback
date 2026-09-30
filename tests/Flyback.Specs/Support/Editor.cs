using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App;
using Flyback.App.Notices;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Inspect;
using Flyback.App.Site;
using Flyback.App.Windows;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;

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
    private ServiceProvider? provider;

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

    /// <summary>Swaps any of the editor's services for the scenario's own. Said before anything opens it.</summary>
    public Action<IServiceCollection>? Services { get; set; }

    /// <summary>Whether the window opens on the scenario's patch, rather than on whatever it starts with itself.</summary>
    public bool OnThePatch { get; set; } = true;

    /// <summary>The size the window opens at, where it is not the one it starts at itself.</summary>
    public Size? Screen { get; set; }

    /// <summary>Opens the window, if it is not open already.</summary>
    public void Open() => Do(_ => { });

    /// <summary>The question up over the window, as its words, or null while there is none.</summary>
    public string? Asking => ReadWindow(open =>
        open.GetVisualDescendants().OfType<ModalOverlay>().FirstOrDefault() is { } dialog
            ? string.Join(" ", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            : null);

    /// <summary>The preset the toolbar says is on the canvas, or null for a document that is none.</summary>
    public string? Showing => ReadWindow(open => (Presets(open).SelectedItem as PatchPreset)?.Name);

    /// <summary>Whether the file and preset controls can replace the patch.</summary>
    public bool CanOpenPatch => ReadWindow(open =>
        open.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "open").IsEnabled
        && open.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "presets-glyph").IsEnabled
        && !Presets(open).IsEnabled);

    /// <summary>Starts the recording state the editor's toolbar and close guard respond to.</summary>
    public void BeginRecording() => DoWindow((_, _) =>
    {
        Service<RecordingState>().SetRunning(true);
        Service<TakeRecording>().Mark();
    });

    /// <summary>Tries the window close action and says whether the window remained open.</summary>
    public bool TryClose() => ReadWindow(open =>
    {
        open.Close();
        return open.IsVisible;
    });

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

    /// <summary>Ctrl+drags a wire off one output and lets it go over another, or over the same one to put it back.</summary>
    public void LiftWire(Guid from, int output, Guid onto, int ontoOutput) =>
        DoWindow((open, canvas) =>
        {
            var patch = canvas.History.Patch;
            var start = OnWindow(open, canvas, NodeGeometry.OutputPort(patch.Find(from)!, output));
            var end = OnWindow(open, canvas, NodeGeometry.OutputPort(patch.Find(onto)!, ontoOutput));

            open.MouseDown(start, MouseButton.Left, RawInputModifiers.Control);
            open.MouseMove(end, RawInputModifiers.Control);
            open.MouseUp(end, MouseButton.Left, RawInputModifiers.Control);
        });

    /// <summary>Carries a module by its title bar to a point of the patch, with <paramref name="modifiers"/> held throughout.</summary>
    public void Carry(Guid node, Point to, RawInputModifiers modifiers = RawInputModifiers.None) =>
        DoWindow((open, canvas) =>
        {
            var found = canvas.History.Patch.Find(node)!;
            var from = OnWindow(open, canvas, new Point(found.X + NodeGeometry.Width / 2, found.Y + NodeGeometry.HeaderHeight / 2));
            var end = OnWindow(open, canvas, to);

            open.MouseDown(from, MouseButton.Left, modifiers);
            open.MouseMove(end, modifiers);
            open.MouseUp(end, MouseButton.Left, modifiers);
        });

    /// <summary>Right-clicks a point of the patch, which over bare canvas or an open group opens the list of modules.</summary>
    public void RightClick(Point at) =>
        DoWindow((open, canvas) =>
        {
            var point = OnWindow(open, canvas, at);

            open.MouseDown(point, MouseButton.Right);
            open.MouseUp(point, MouseButton.Right);
        });

    /// <summary>
    /// Puts a finger down at each start, a point on the canvas's own control, moves them
    /// all to their ends together in a few steps, and lifts them in the order they went down.
    /// </summary>
    public void Touch(params (Point From, Point To)[] fingers) =>
        DoWindow((_, canvas) =>
        {
            const int steps = 8;

            var pointers = fingers.Select(_ => new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, false)).ToArray();
            ulong time = 1_000;

            for (var i = 0; i < fingers.Length; i++) canvas.Fingers.Down(canvas, pointers[i], fingers[i].From, time += 10);

            for (var step = 1; step <= steps; step++)
                for (var i = 0; i < fingers.Length; i++)
                    canvas.Fingers.Move(canvas, pointers[i], fingers[i].From + (fingers[i].To - fingers[i].From) * step / steps);

            for (var i = 0; i < fingers.Length; i++) canvas.Fingers.Up(canvas, pointers[i], fingers[i].To, time += 10);
        });

    /// <summary>Holds a finger still at a point of the patch until it is the right button, and lifts it.</summary>
    public void HoldFinger(Point graph) =>
        DoWindow((_, canvas) =>
        {
            var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
            var at = canvas.GraphToScreen.Transform(graph);

            canvas.Fingers.Down(canvas, finger, at, 1_000);
            canvas.Fingers.Held();
            canvas.Fingers.Up(canvas, finger, at, 1_000 + (ulong)Fingers.HoldTime.TotalMilliseconds);
        });

    private static Point OnWindow(MainWindow open, NodeEditor canvas, Point graph) =>
        canvas.TranslatePoint(canvas.GraphToScreen.Transform(graph), open)!.Value;

    /// <summary>Whether the open list of modules has its filter box holding the keyboard.</summary>
    public bool ListTakesKeys => ReadWindow(open =>
        (open.GetVisualDescendants().OfType<ModulePalette>().FirstOrDefault()
            ?? throw new InvalidOperationException("no list of modules is open"))
        .GetVisualDescendants().OfType<TextBox>().First().IsFocused);

    /// <summary>Taps a finger on a point of the patch.</summary>
    public void TapFinger(Point graph) =>
        DoWindow((_, canvas) =>
        {
            var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
            var at = canvas.GraphToScreen.Transform(graph);

            canvas.Fingers.Down(canvas, finger, at, 1_000);
            canvas.Fingers.Up(canvas, finger, at, 1_050);
        });

    /// <summary>Zooms the view as far out as it goes, about the middle of the canvas.</summary>
    public void ZoomAllTheWayOut() =>
        Do(canvas => canvas.View.ZoomAt(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), -100));

    /// <summary>The names of the toolbar's shown buttons that are not wholly inside the window.</summary>
    public IReadOnlyList<string?> ToolbarButtonsOffScreen => ReadWindow(open =>
        Service<Toolbar>().View.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && !Inside(open, b))
            .Select(b => b.Name)
            .ToList());

    /// <summary>Whether the button the module panel names <paramref name="name"/> is shown and wholly inside the window.</summary>
    public bool PanelButtonOnScreen(string name) => ReadWindow(open =>
        open.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == name) is { IsEffectivelyVisible: true } button
        && Inside(open, button));

    /// <summary>Presses the button the module panel names <paramref name="name"/>.</summary>
    public void PressPanelButton(string name) =>
        DoWindow((open, _) =>
            open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    /// <summary>What the module panel says, all its words together.</summary>
    public string PanelText => ReadWindow(open =>
        string.Join(" ", open.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "inspector")
            .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));

    /// <summary>How wide the report line at the foot of the window is laid out, beside the counts as they stand now.</summary>
    public double ReportWidth => ReadWindow(open =>
    {
        Service<StatusBar>().Update();
        open.UpdateLayout();

        return open.GetVisualDescendants().OfType<ReportLine>().Single().Bounds.Width;
    });

    /// <summary>Opens the preset gallery from the toolbar.</summary>
    public void OpenGallery() =>
        DoWindow((open, _) =>
            open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "presets-glyph")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    /// <summary>Whether the open preset gallery's filter box holds the keyboard.</summary>
    public bool GalleryTakesKeys => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "preset-filter").IsFocused);

    /// <summary>The answers the question up over the window offers, and whether each is wholly inside the window.</summary>
    public IReadOnlyList<(string Label, bool OnScreen)> Answers => ReadWindow(open =>
        open.GetVisualDescendants().OfType<ModalOverlay>().Single()
            .GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content is string)
            .Select(b => ((string)b.Content!, Inside(open, b)))
            .ToList());

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

    /// <summary>Which edge of the full-screen picture the transport waits at, or null while it is not showing.</summary>
    public Avalonia.Layout.VerticalAlignment? TransportEdge => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.VerticalAlignment
            : (Avalonia.Layout.VerticalAlignment?)null);

    /// <summary>Which edge of the full-screen picture the knobs wait at.</summary>
    public Avalonia.Layout.VerticalAlignment KnobsEdge => ReadWindow(open =>
        open.GetVisualDescendants().OfType<StageKnobs>().Single().VerticalAlignment);

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

    /// <summary>Clicks the toolbar's Volume at <paramref name="share"/> of the way along it.</summary>
    public void ClickVolume(double share) =>
        DoWindow((open, _) =>
        {
            var slider = Volume(open);
            var at = slider.TranslatePoint(new Point(slider.Bounds.Width * share, slider.Bounds.Height / 2), open)!.Value;

            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
        });

    /// <summary>Whether the toolbar's Volume can be turned.</summary>
    public bool CanTurnVolume => ReadWindow(open => Volume(open).IsEffectivelyEnabled);

    private static Slider Volume(MainWindow window) =>
        window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "volume");

    /// <summary>Where the patch's clock is, in seconds: what the status bar says after "t =".</summary>
    public double Clock => ReadWindow(open => open.GetVisualDescendants().OfType<PreviewHost>().First().Time);

    /// <summary>Whether the toolbar offers to play rather than to pause.</summary>
    public bool Paused => ReadWindow(open => ToolTip.GetTip(open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "pause")) as string == Toolbar.PlayTip);

    /// <summary>The names of the buttons on the toolbar, left to right.</summary>
    public IReadOnlyList<string?> ToolbarButtons => ReadWindow(_ =>
        Service<Toolbar>().View.GetVisualDescendants().OfType<Button>().Select(b => b.Name).ToList());

    /// <summary>Presses a toolbar toggle into <paramref name="on"/>, as a click does.</summary>
    public void Toggle(string name, bool on) => DoWindow((open, _) => Toggler(open, name).IsChecked = on);

    /// <summary>Whether a toolbar toggle is pressed in, and whether it can be pressed at all.</summary>
    public (bool On, bool Enabled) Toggled(string name) => ReadWindow(open =>
    {
        var toggle = Toggler(open, name);

        return (toggle.IsChecked == true, toggle.IsEnabled);
    });

    /// <summary>How wide the canvas is laid out.</summary>
    public double CanvasWidth => Read(canvas => canvas.Bounds.Width);

    /// <summary>How wide the preview is laid out.</summary>
    public double PreviewWidth => ReadWindow(open => open.GetVisualDescendants().OfType<PreviewHost>().Single().Bounds.Width);

    private static ToggleButton Toggler(MainWindow open, string name) =>
        open.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == name);

    /// <summary>The size the picture is drawn at, before it is scaled to its box.</summary>
    public PixelSize PictureSize => ReadWindow(open => open.GetVisualDescendants().OfType<PreviewHost>().First().Resolution);

    /// <summary>Whether the picture has the whole window.</summary>
    public bool PictureFullScreen => ReadWindow(_ => Service<FullScreenPreview>().IsFullScreen);

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

    /// <summary>Puts text on the clipboard, as copying it anywhere else would.</summary>
    public void Clip(string text) =>
        Run(async () =>
        {
            var clipboard = TopLevel.GetTopLevel(Window())?.Clipboard
                ?? throw new InvalidOperationException("no clipboard under the window");

            await clipboard.SetTextAsync(text);
            return true;
        });

    /// <summary>Shows the text view, writes <paramref name="source"/> into it and applies it, which makes the text the document.</summary>
    public void ApplyText(string source)
    {
        // Its own turn, since the text view is laid out only once it is showing.
        DoWindow((open, _) => open.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "code").IsChecked = true);

        DoWindow((open, _) =>
        {
            Source(open).Text = source;
            open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }

    /// <summary>What the text view holds.</summary>
    public string Text => ReadWindow(open => Source(open).Text);

    /// <summary>The open patch as text, as a page's <c>flyback.text()</c> reads it.</summary>
    public string ScriptedText => ReadWindow(_ => Service<Document>().AsText());

    /// <summary>Applies text as a page's <c>flyback.apply</c> does: null once applied, or what is wrong with it.</summary>
    public string? ApplyScripted(string source)
    {
        string? answer = null;

        DoWindow((_, _) => answer = Service<Document>().Apply(source));

        return answer;
    }

    /// <summary>
    /// Opens the gallery and waits for the preset site to answer or not, then says each
    /// tile it lists under the site's heading: its name and its stars as the tile reads them.
    /// </summary>
    public IReadOnlyList<(string Name, string Stars)> SharedInGallery() =>
        Run(async () =>
        {
            var open = Window();

            if (!open.GetVisualDescendants().OfType<ModalOverlay>().Any())
                open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "presets-glyph").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => open.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "site-status") is { Text: not "Looking…" });

            return (IReadOnlyList<(string, string)>)[.. SharedTiles(open).Select(tile => (
                ((SitePreset)tile.Tag!).Name,
                tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "siteRating").Inlines!.Text ?? string.Empty))];
        });

    /// <summary>Picks a tile the gallery lists under the site's heading, and waits for the editor to say what came of it.</summary>
    public void PickShared(string name) =>
        Run(async () =>
        {
            var open = Window();
            var report = open.GetVisualDescendants().OfType<ReportLine>().Single();

            // Counted rather than looked for after a position: a line said again moves to the end.
            int Answered() => report.History.Count(line => line.Contains($"“{name}”", StringComparison.Ordinal)
                && !line.StartsWith("Downloading", StringComparison.Ordinal));

            var before = Answered();

            SharedTiles(open).Single(tile => ((SitePreset)tile.Tag!).Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(
                () => Answered() > before,
                () => $"the editor to say what came of picking “{name}”. The report line said: {string.Join(" | ", report.History)}. "
                    + $"Up over the window: {open.GetVisualDescendants().OfType<ModalOverlay>().Count()} dialog(s).");

            context.Replace(Canvas().History.Patch);
            return true;
        });

    private static IEnumerable<Button> SharedTiles(MainWindow open) =>
        open.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "site-tile");

    /// <summary>Gives the UI thread turns until <paramref name="done"/>, for at most thirty seconds.</summary>
    /// <param name="waitingFor">What is said on giving up, read then.</param>
    private async Task Until(Func<bool> done, Func<string>? waitingFor = null)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (!done())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Waited thirty seconds for {waitingFor?.Invoke() ?? "the editor"}");

            Settle();
            await Task.Delay(5);
        }
    }

    /// <summary>Opens a shared preset's file as a page sent one by <c>?file=</c> does.</summary>
    public void OpenShared(string name, string fileName, byte[] bytes) =>
        Run(async () =>
        {
            await Service<Flyback.App.Gallery.PresetSlot>().OpenSharedAsync(name, fileName, bytes);
            return true;
        });

    /// <summary>Presses Ctrl+V in the text view, with the caret at the end of the text.</summary>
    public void PasteIntoText() =>
        DoWindow((open, _) =>
        {
            var text = Source(open);

            text.CaretOffset = text.Document.TextLength;
            text.TextArea.Focus();

            open.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
            open.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Control);
        });

    private static AvaloniaEdit.TextEditor Source(MainWindow open) =>
        open.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single(t => t.Name == "source");

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

        provider = EditorServices.Provider(Setup, Services);
        window = provider.Window();

        if (Screen is { } screen)
        {
            window.Width = screen.Width;
            window.Height = screen.Height;
        }

        window.Start();

        window.Show();
        Settle();

        var canvas = CanvasIn(window);

        canvas.Reactions.Add<CanvasSaid>(notice => said.Add(notice.Message));
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

    private T Service<T>() where T : notnull => provider!.GetRequiredService<T>();

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

    /// <summary>Whether <paramref name="control"/> is laid out wholly inside the window.</summary>
    private static bool Inside(MainWindow window, Control control)
    {
        var bounds = control.Bounds;

        return control.TranslatePoint(default, window) is { } at
            && at.X >= -0.5 && at.Y >= -0.5
            && at.X + bounds.Width <= window.Bounds.Width + 0.5
            && at.Y + bounds.Height <= window.Bounds.Height + 0.5;
    }

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
