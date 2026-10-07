using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
using Flyback.Ui;
using Flyback.Editor.Notices;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Inspect;
using Flyback.Editor.Site;
using Flyback.Editor.Windows;
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
public sealed class EditorDriver(PatchContext context, HeadlessTurn turn) : IDisposable
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
        Named<Button>(open, "open").IsEnabled
        && Named<Button>(open, "presets-glyph").IsEnabled
        && !Presets(open).IsEnabled);

    /// <summary>Starts the recording state the editor's toolbar and close guard respond to.</summary>
    public void BeginRecording() => DoWindow((_, _) =>
    {
        Service<RecordingState>().SetRunning(true);
        Service<TakeRecording>().Mark();
    });

    /// <summary>Puts the assistant mid-turn, as the panel does while a message is being answered.</summary>
    public void AssistantStartsWorking() =>
        DoWindow((_, _) => Service<AssistantConversation>().Working = true);

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

    /// <summary>How bright the canvas under the module was before it was picked up.</summary>
    private int restingBrightness;

    /// <summary>Presses a module by its title bar and holds the button down, as the first moment of carrying it.</summary>
    public void PickUp(Guid node) =>
        DoWindow((open, canvas) =>
        {
            var found = canvas.History.Patch.Find(node)!;

            restingBrightness = BrightnessUnder(open, canvas, found);
            open.MouseDown(OnWindow(open, canvas, new Point(found.X + NodeGeometry.Width / 2, found.Y + NodeGeometry.HeaderHeight / 2)), MouseButton.Left);
        });

    /// <summary>Whether the canvas under a module is darker than it was before the module was picked up.</summary>
    public bool ShadowUnder(Guid node) =>
        Run(() =>
        {
            Settle();

            return BrightnessUnder(Window(), Canvas(), Canvas().History.Patch.Find(node)!) < restingBrightness;
        });

    /// <summary>The summed brightness of the strip of canvas just under a module, as the window drew it.</summary>
    private static int BrightnessUnder(MainWindow open, NodeEditor canvas, NodeInstance module)
    {
        var bounds = canvas.Geometry.Bounds(module, NodeCatalog.BuiltIn.Require(module.TypeId));
        var from = OnWindow(open, canvas, new Point(bounds.X + 10, bounds.Bottom + 3));
        var to = OnWindow(open, canvas, new Point(bounds.X + 40, bounds.Bottom + 8));

        using var frame = open.CaptureRenderedFrame() ?? throw new InvalidOperationException("the window rendered nothing");
        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var sum = 0;

        for (var y = (int)from.Y; y < (int)to.Y; y++)
        for (var x = (int)from.X; x < (int)to.X; x++)
        {
            var at = y * locked.RowBytes + x * 4;

            sum += bytes[at] + bytes[at + 1] + bytes[at + 2];
        }

        return sum;
    }

    /// <summary>Drags <paramref name="button"/> from one point of the patch to another, in one move.</summary>
    public void DragCanvas(Point from, Point to, MouseButton button) =>
        DoWindow((open, canvas) =>
        {
            var start = OnWindow(open, canvas, from);
            var end = OnWindow(open, canvas, to);

            open.MouseDown(start, button);
            open.MouseMove(end);
            open.MouseUp(end, button);
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

    /// <summary>How many rows the toolbar's shown buttons stand in.</summary>
    public int ToolbarRows => ReadWindow(open =>
    {
        var bar = Service<Toolbar>().View;

        return bar.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible)
            .Select(b => Math.Round(b.TranslatePoint(default, bar)!.Value.Y / 10))
            .Distinct()
            .Count();
    });

    /// <summary>What the toolbar's menu offers, in order, or nothing while the menu's button is hidden.</summary>
    public IReadOnlyList<string> ToolbarMenu => ReadWindow(_ =>
    {
        var overflow = Service<Toolbar>().Overflow;

        if (!overflow.More.IsEffectivelyVisible) return [];

        var items = OpenToolbarMenu(overflow);

        overflow.More.Flyout!.Hide();
        return items.Select(item => item.Header as string ?? string.Empty).ToList();
    });

    /// <summary>Picks the item so labeled from the toolbar's menu, and lets whatever it opens come up.</summary>
    public void PickFromToolbarMenu(string label) =>
        DoWindow((open, _) =>
        {
            var item = OpenToolbarMenu(Service<Toolbar>().Overflow).SingleOrDefault(item => item.Header as string == label)
                       ?? throw new InvalidOperationException($"the toolbar's menu has no {label}");

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            for (var turn = 0; turn < 20 && !open.GetVisualDescendants().OfType<ModalOverlay>().Any(); turn++)
                Dispatcher.UIThread.RunJobs();
        });

    /// <summary>Whether the settings are up over the window.</summary>
    public bool SettingsUp => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TabControl>().Any(tabs => tabs.Name == "settingsTabs"));

    /// <summary>Opens the toolbar's menu and reads the items it shows, as drawn in its popup.</summary>
    private static List<MenuItem> OpenToolbarMenu(ToolbarOverflow overflow)
    {
        var menu = (MenuFlyout)overflow.More.Flyout!;

        menu.ShowAt(overflow.More);
        Dispatcher.UIThread.RunJobs();

        if (!menu.IsOpen) throw new InvalidOperationException("the toolbar's menu did not open");

        // Only an item laid out in the open popup is one a finger can reach.
        return (menu.ItemsSource ?? Array.Empty<object>()).OfType<MenuItem>().Where(item => TopLevel.GetTopLevel(item) is not null).ToList();
    }

    /// <summary>The names of the transport row's shown buttons, left to right.</summary>
    public IReadOnlyList<string?> TransportButtons => ReadWindow(_ =>
        Service<TransportRow>().View.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible)
            .Select(b => b.Name)
            .ToList());

    /// <summary>The names of the transport row's shown buttons that are not wholly inside the window, or smaller than a finger.</summary>
    public IReadOnlyList<string?> TransportButtonsOutOfReach => ReadWindow(open =>
        Service<TransportRow>().View.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible
                        && (!Inside(open, b) || b.Bounds.Width < TransportRow.Reach || b.Bounds.Height < TransportRow.Reach))
            .Select(b => b.Name)
            .ToList());

    /// <summary>How wide the seek bar is laid out.</summary>
    public double SeekBarWidth => ReadWindow(open => SeekBar(open).Bounds.Width);

    /// <summary>Whether the transport row is shown, and whether the playhead line standing in for it is.</summary>
    public (bool Row, bool Line) TransportShown => ReadWindow(open =>
        (Named<Border>(open, "transport").IsEffectivelyVisible, Service<TransportRow>().Seek.Line.IsEffectivelyVisible));

    /// <summary>Whether what the transport row folds away while narrow is on the screen, behind its button or in the row.</summary>
    public bool LengthOnScreen => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Name == "seekLength") is { IsEffectivelyVisible: true } box
        && Inside(open, box));

    /// <summary>Opens what the narrow transport row keeps behind its button.</summary>
    public void OpenTransportMore() =>
        DoWindow((open, _) =>
        {
            var more = Named<Button>(open, "transport-more");

            (more.Flyout ?? throw new InvalidOperationException("the transport row's button opens nothing")).ShowAt(more);
        });

    /// <summary>How tall the canvas is laid out.</summary>
    public double CanvasHeight => Read(canvas => canvas.Bounds.Height);

    /// <summary>Presses a finger on the seek bar where <paramref name="from"/> falls, slides it to <paramref name="to"/>, and lifts it.</summary>
    public void SlideFingerAlongSeekBar(double from, double to) =>
        DoWindow((open, _) =>
        {
            var bar = SeekBar(open);
            var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
            var down = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var moving = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var up = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            Point On(double seconds) => bar.TranslatePoint(bar.At(seconds), open)!.Value;

            bar.RaiseEvent(new PointerPressedEventArgs(bar, finger, open, On(from), 1_000, down, KeyModifiers.None));

            for (var step = 1; step <= 4; step++)
                bar.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, bar, finger, open, On(from + (to - from) * step / 4), 1_000 + (ulong)step * 10, moving, KeyModifiers.None));

            bar.RaiseEvent(new PointerReleasedEventArgs(bar, finger, open, On(to), 1_100, up, KeyModifiers.None, MouseButton.Left));
        });

    /// <summary>Whether the button the module panel names <paramref name="name"/> is shown and wholly inside the window.</summary>
    public bool PanelButtonOnScreen(string name) => ReadWindow(open =>
        open.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == name) is { IsEffectivelyVisible: true } button
        && Inside(open, button));

    /// <summary>Presses the button the module panel names <paramref name="name"/>.</summary>
    public void PressPanelButton(string name) =>
        DoWindow((open, _) =>
            Named<Button>(open, name)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    /// <summary>Makes a randomize reach anywhere and land at once, on a seeded die.</summary>
    public void RandomizeAtOnce() =>
        DoWindow((_, _) =>
        {
            Service<Flyback.Editor.Settings.OutputSettingRepository>().Current.Randomize = new RandomizeSettings { Amount = 1, GlideSeconds = 0 };
            Service<Flyback.Editor.Knobs.KnobRandomizer>().Random = new Random(1);
        });

    /// <summary>Where the editor's panel knob called <paramref name="name"/> rests.</summary>
    public float KnobAt(string name) => Read(canvas => canvas.History.Patch.Controls!.Single(control => control.Name == name).Value);

    /// <summary>What the module panel says, all its words together.</summary>
    public string PanelText => ReadWindow(open =>
        string.Join(" ", Named<StackPanel>(open, "inspector")
            .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));

    /// <summary>Whether the shortcut group the empty panel calls <paramref name="title"/> shows its rows.</summary>
    public bool ShortcutGroupOpen(string title) => ReadWindow(open =>
        Named<Button>(open, "shortcuts:" + title).Parent is Panel { Children: [_, { IsVisible: true }] });

    /// <summary>Presses the heading of the shortcut group the empty panel calls <paramref name="title"/>.</summary>
    public void PressShortcutGroup(string title) =>
        DoWindow((open, _) =>
            Named<Button>(open, "shortcuts:" + title)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

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
            Named<Button>(open, "presets-glyph")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    /// <summary>Chooses the open gallery's card for the preset called <paramref name="name"/>.</summary>
    public void ChooseCard(string name) =>
        DoWindow((open, _) => Card(open, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    /// <summary>The type of every module on the canvas, as it is now.</summary>
    public IReadOnlyList<string> CanvasTypes => ReadWindow(open => (IReadOnlyList<string>)[.. CanvasIn(open).History.Patch.Nodes.Select(node => node.TypeId)]);

    /// <summary>The name the gallery's column beside the cards describes.</summary>
    public string? Described => ReadWindow(open => Named<TextBlock>(open, "detail-name").Text);

    /// <summary>Presses the gallery's button that opens the chosen card, and waits for the gallery to come down.</summary>
    public void UseChosenCard() =>
        Run(async () =>
        {
            var open = Window();

            Named<Button>(open, "use-preset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => !open.GetVisualDescendants().OfType<ModalOverlay>().Any(), () => $"the gallery to close. {Situation(open)}");

            context.Replace(Canvas().History.Patch);
            return true;
        });

    /// <summary>Presses the row of the gallery's left column labeled <paramref name="label"/>.</summary>
    public void PressGalleryRow(string label) =>
        DoWindow((open, _) => open.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "filter-row" && (string)b.Tag! == label)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    /// <summary>The presets whose cards the open gallery shows, once every card has said what it works with.</summary>
    public IReadOnlyList<string> CardsShown =>
        Run(async () =>
        {
            var open = Window();
            var cards = open.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "tile").ToList();

            await Until(
                () => cards.All(card => Named<StackPanel>(card, "badges").Children.Count > 0),
                () => $"every card to say what it works with. {Situation(open)}");

            return (IReadOnlyList<string>)[.. cards.Where(card => card.IsEffectivelyVisible).Select(card => ((PatchPreset)card.Tag!).Name)];
        });

    private static Button Card(MainWindow open, string name) =>
        open.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "tile" && ((PatchPreset)b.Tag!).Name == name)
        ?? throw new InvalidOperationException($"the gallery has no card for {name}");

    /// <summary>Whether the open preset gallery has a section for the preset site.</summary>
    public bool GalleryListsSite => ReadWindow(open =>
        open.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "site-presets"));

    /// <summary>Whether the open preset gallery has the card to start from a prompt.</summary>
    public bool GalleryOffersPrompt => ReadWindow(open =>
        open.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "prompt-card"));

    /// <summary>What the gallery's prompt card holds.</summary>
    public string PromptText => ReadWindow(open => Named<TextBox>(open, "prompt-text").Text ?? string.Empty);

    /// <summary>Types an idea into the gallery's prompt card.</summary>
    public void TypePrompt(string idea) =>
        DoWindow((open, _) => Named<TextBox>(open, "prompt-text").Text = idea);

    /// <summary>Presses Expand on the prompt card and waits for what it holds to change.</summary>
    public void ExpandPrompt() =>
        Run(async () =>
        {
            var open = Window();
            var words = Named<TextBox>(open, "prompt-text");
            var idea = words.Text;

            Named<Button>(open, "expand-prompt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => words.Text != idea, () => $"the assistant to write the idea out. {Situation(open)}");

            return true;
        });

    /// <summary>Presses Start on the prompt card and waits until <paramref name="sent"/> says the assistant has the prompt.</summary>
    public void StartPrompt(Func<bool> sent) =>
        Run(async () =>
        {
            var open = Window();

            Named<Button>(open, "start-prompt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(sent, () => $"the prompt to reach the assistant. {Situation(open)}");

            context.Replace(Canvas().History.Patch);
            return true;
        });

    /// <summary>What the assistant's box holds.</summary>
    public string MessageText => ReadWindow(open => Named<TextBox>(open, "instruction").Text ?? string.Empty);

    /// <summary>Types a message into the assistant's box.</summary>
    public void TypeMessage(string message) =>
        DoWindow((open, _) => Named<TextBox>(open, "instruction").Text = message);

    /// <summary>Presses Expand beside the assistant's box and waits for what it holds to change.</summary>
    public void ExpandMessage() =>
        Run(async () =>
        {
            var open = Window();
            var box = Named<TextBox>(open, "instruction");
            var typed = box.Text;

            Named<Button>(open, "expand-message").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => box.Text != typed && !box.IsReadOnly, () => $"the assistant to write the message out. {Situation(open)}");

            return true;
        });

    /// <summary>Presses Expand beside the assistant's box and waits for the box to be free again, whatever it then holds.</summary>
    public void ExpandMessageOnceDone() =>
        Run(async () =>
        {
            var open = Window();
            var box = Named<TextBox>(open, "instruction");

            Named<Button>(open, "expand-message").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => !box.IsReadOnly, () => $"the assistant to finish writing the message out. {Situation(open)}");

            return true;
        });

    /// <summary>Every line of the assistant's transcript, as it is kept.</summary>
    public IReadOnlyList<string> TranscriptLines => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TranscriptView>().Single().Lines.Select(line => line.Text).ToList());

    /// <summary>Whether the assistant's column is open beside the canvas.</summary>
    public bool AssistantColumnOpen => ReadWindow(open =>
        open.GetVisualDescendants().OfType<AssistantPanel>().Single().IsVisible);

    /// <summary>Whether the status bar shows the letter and the rule before it.</summary>
    public (bool Letter, bool Rule) StatusBarLetter => ReadWindow(open => (
        Named<Button>(open, "letter").IsVisible,
        Named<TextBlock>(open, "statusRule").IsVisible));

    /// <summary>Whether the open preset gallery's filter box holds the keyboard.</summary>
    public bool GalleryTakesKeys => ReadWindow(open =>
        Named<TextBox>(open, "preset-filter").IsFocused);

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
            var title = Named<TextBlock>(open, "moduleName");
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
            Named<Button>(open, "settings")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The dialog is put up on a later turn than the click.
            for (var turn = 0; turn < 20 && !open.GetVisualDescendants().OfType<ModalOverlay>().Any(); turn++)
                Dispatcher.UIThread.RunJobs();

            Settle();

            var tabs = Named<TabControl>(open, "settingsTabs");

            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => (item.Header as TextBlock)?.Text == tab);
        });

    /// <summary>Ticks or clears the box so labeled on the question up over the window.</summary>
    public void Tick(string label, bool on) =>
        DoWindow((open, _) =>
            open.GetVisualDescendants().OfType<ModalOverlay>().Single()
                .GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == label)
                .IsChecked = on);

    /// <summary>Types a number into the box so named on the question up over the window.</summary>
    public void Type(string name, double value) =>
        DoWindow((open, _) =>
            open.GetVisualDescendants().OfType<ModalOverlay>().Single()
                .GetVisualDescendants().OfType<NumericUpDown>().Single(box => box.Name == name)
                .Value = (decimal)value);

    /// <summary>
    /// Drags the panel knob called <paramref name="name"/> by its name and lets go over
    /// the knob called <paramref name="onto"/>, at <paramref name="across"/> of its width.
    /// </summary>
    public void DropKnob(string name, string onto, double across) =>
        DoWindow((open, _) =>
        {
            var names = open.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Name == "knob-name" && text.FindAncestorOfType<Flyback.Editor.Knobs.ControlsPanel>() is not null)
                .ToList();
            var held = names.Single(text => text.Text == name);
            var cell = names.Single(text => text.Text == onto).FindAncestorOfType<Border>()!;
            var from = held.TranslatePoint(new Point(held.Bounds.Width / 2, held.Bounds.Height / 2), open)!.Value;
            var to = cell.TranslatePoint(new Point(cell.Bounds.Width * across, cell.Bounds.Height / 2), open)!.Value;

            open.MouseDown(from, MouseButton.Left);
            open.MouseMove(from + new Point(10, 0));
            open.MouseMove(to);
            open.MouseUp(to, MouseButton.Left);
        });

    /// <summary>The knob panel's knobs by name, a row at a time from the top, each left to right.</summary>
    public IReadOnlyList<string> KnobRows => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TextBlock>()
            .Where(name => name.Name == "knob-name" && name.FindAncestorOfType<Flyback.Editor.Knobs.ControlsPanel>() is not null)
            .Select(name => (Name: name.Text ?? string.Empty, At: name.TranslatePoint(default, open)!.Value))
            .GroupBy(knob => Math.Round(knob.At.Y))
            .OrderBy(row => row.Key)
            .Select(row => string.Join(" ", row.OrderBy(knob => knob.At.X).Select(knob => knob.Name)))
            .ToList());

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
            var loop = Named<ToggleButton>(open, "seekLoop");
            var at = loop.TranslatePoint(new Point(loop.Bounds.Width / 2, loop.Bounds.Height / 2), open)!.Value;

            open.MouseDown(at, MouseButton.Left);
            open.MouseUp(at, MouseButton.Left);
        });

    /// <summary>
    /// Waits for the patch's clock to come to <paramref name="arrived"/>, for at most
    /// <paramref name="cap"/>, and throws saying where it got to where it did not.
    /// </summary>
    /// <remarks>
    /// The clock moves on the UI thread's timers, and headless fires those only while a
    /// dispatch is waiting on the thread, so each look waits there rather than beside it.
    /// </remarks>
    public void WaitForClock(Func<double, bool> arrived, TimeSpan cap)
    {
        var until = DateTime.UtcNow + cap;

        while (!arrived(Clock))
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Waited {cap.TotalSeconds:0.#} s for the clock, which reads {Clock:0.000} s, {(Paused ? "paused" : "playing")}. {ReadWindow(Situation)}");

            Run(async () =>
            {
                await Task.Delay(20);
                return true;
            });
        }

    }

    /// <summary>Waits for the patch to stop by itself, for at most <paramref name="cap"/>, and throws saying where it got to where it did not.</summary>
    public void WaitForStop(TimeSpan cap)
    {
        var until = DateTime.UtcNow + cap;

        while (!Paused)
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Waited {cap.TotalSeconds:0.#} s for the patch to stop by itself; its clock reads {Clock:0.000} s. {ReadWindow(Situation)}");

            Run(async () =>
            {
                await Task.Delay(20);
                return true;
            });
        }
    }

    /// <summary>Which edge of the full-screen picture the transport waits at, or null while it is not showing.</summary>
    public Avalonia.Layout.VerticalAlignment? TransportEdge => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.VerticalAlignment
            : (Avalonia.Layout.VerticalAlignment?)null);

    /// <summary>Whether the full-screen transport has a sound button, or null while it is not showing.</summary>
    public bool? TransportHasSound => ReadWindow(open =>
        open.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.HasSound
            : (bool?)null);

    /// <summary>Which edge of the full-screen picture the knobs wait at.</summary>
    public Avalonia.Layout.VerticalAlignment KnobsEdge => ReadWindow(open =>
        open.GetVisualDescendants().OfType<StageKnobs>().Single().VerticalAlignment);

    /// <summary>Types a length into the box beside the seek bar, and presses Enter.</summary>
    public void SetSeekLength(string typed) =>
        DoWindow((open, _) =>
        {
            var box = Named<TextBox>(open, "seekLength");

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

    /// <summary>Whether the toolbar shows the Output's Volume at all.</summary>
    public bool ShowsVolume => ReadWindow(open => Volume(open).IsEffectivelyVisible);

    /// <summary>How many times real time the editor says the sound renders at, or 0 while it has not measured it.</summary>
    public double SoundSpeed => ReadWindow(_ => Service<Playback>().SoundSpeed);

    /// <summary>What the status bar says the patch costs, written afresh.</summary>
    public string StatusCount => ReadWindow(open =>
    {
        Service<StatusBar>().Update();
        return Named<TextBlock>(open, "statusCount").Text ?? string.Empty;
    });

    private static Slider Volume(MainWindow window) =>
        Named<Slider>(window, "volume");

    /// <summary>Where the patch's clock is, in seconds: what the status bar says after "t =".</summary>
    public double Clock => ReadWindow(open => open.GetVisualDescendants().OfType<PreviewHost>().First().Time);

    /// <summary>Whether the toolbar offers to play rather than to pause.</summary>
    public bool Paused => ReadWindow(open => ToolTip.GetTip(Named<Button>(open, "pause")) as string == TransportRow.PlayTip);

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
        Named<ToggleButton>(open, name);

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

    /// <summary>Taps the picture once, as a click or a finger does.</summary>
    public void TapPicture() =>
        DoWindow((open, _) =>
        {
            var preview = open.GetVisualDescendants().OfType<PreviewHost>().Single();
            var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), open)!.Value;

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

    /// <summary>Deletes the first module on the canvas that is not the Output, as selecting it and pressing Delete does.</summary>
    public void DeleteAModule()
    {
        var module = CanvasIn(window!).History.Patch.Nodes.First(node => node.TypeId != NodeCatalog.OutputTypeId);

        Select(module.Id);
        Press(PhysicalKey.Delete);
    }

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

    /// <summary>Presses Ctrl+M and waits for the measurement to be pinned.</summary>
    public void Measure()
    {
        PressCtrl(PhysicalKey.M);

        Run(async () =>
        {
            await Until(
                () => Service<MeasureLabels>().Report is not null && !Service<Measuring>().Running,
                () => "the measurement to be pinned");

            return true;
        });
    }

    /// <summary>What the last measurement pinned on one output, or null where nothing is.</summary>
    internal Flyback.Engine.Measure.Measurement? Measured(Guid node, int port) => Run(() => Service<MeasureLabels>().Of(node, port));

    /// <summary>Sets Settings → Canvas → Measure for, and saves it.</summary>
    public void MeasureFor(double seconds) =>
        Run(() =>
        {
            var section = Service<CanvasSection>();

            section.View.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "measureWindow").SelectedIndex =
                CanvasSettings.MeasureWindows.ToList().IndexOf(seconds);
            section.Save();
            return true;
        });

    /// <summary>How long the pinned measurement ran for.</summary>
    public double? MeasuredSeconds => Run(() => Service<MeasureLabels>().Report?.Seconds);

    /// <summary>Whether the pinned measurement is out of date.</summary>
    public bool MeasurementStale => Run(() => Service<MeasureLabels>().Stale);

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
        DoWindow((open, _) => Named<ToggleButton>(open, "code").IsChecked = true);

        DoWindow((open, _) =>
        {
            Source(open).Text = source;
            Named<Button>(open, "apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }

    /// <summary>What the text view holds.</summary>
    public string Text => ReadWindow(open => Source(open).Text);

    /// <summary>Whether leaving would lose anything, which a page asks about before it is left.</summary>
    public bool SomethingToLose => ReadWindow(_ => Service<UnsavedWork>().SomethingToLose);

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
                Named<Button>(open, "presets-glyph").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            TextBlock? Status() => open.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "site-status");

            await Until(
                () => Status() is { Text: not "Looking…" },
                () => $"the gallery to hear from the preset site. Its line on the site says: {Status()?.Text ?? "nothing, having no site section"}. {Situation(open)}");

            return (IReadOnlyList<(string, string)>)[.. SharedTiles(open).Select(tile => (
                ((SitePreset)tile.Tag!).Name,
                Named<TextBlock>(tile, "siteRating").Inlines!.Text ?? string.Empty))];
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

            var tile = SharedTiles(open).SingleOrDefault(tile => ((SitePreset)tile.Tag!).Name == name)
                ?? throw new InvalidOperationException(
                    $"The gallery lists no “{name}” from the preset site; it lists: {string.Join(", ", SharedTiles(open).Select(t => ((SitePreset)t.Tag!).Name))}. {Situation(open)}");

            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => Answered() > before, () => $"the editor to say what came of picking “{name}”. {Situation(open)}");

            context.Replace(Canvas().History.Patch);
            return true;
        });

    /// <summary>Whether the gallery's tile for a shared preset can be picked, and what it says the page lacks to open it.</summary>
    public (bool Pickable, string? Lacks) SharedTile(string name)
    {
        var listed = SharedInGallery();

        return ReadWindow(open =>
        {
            var tile = SharedTiles(open).SingleOrDefault(tile => ((SitePreset)tile.Tag!).Name == name)
                ?? throw new InvalidOperationException(
                    $"The gallery lists no “{name}” from the preset site; it lists: {string.Join(", ", listed.Select(t => t.Name))}. {Situation(open)}");

            return (tile.IsEnabled, tile.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "site-lacks")?.Text);
        });
    }

    /// <summary>Writes a letter from the status bar's glyph and sends it, then waits for the editor to say what came of it.</summary>
    public void SendLetter(string mood, string message) =>
        Run(async () =>
        {
            var open = Window();

            Named<Button>(open, "letter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();

            var letter = Named<StackPanel>(open, "letter");

            letter.GetVisualDescendants().OfType<RadioButton>().Single(choice => (string?)choice.Tag == mood).IsChecked = true;
            Named<TextBox>(letter, "message").Text = message;
            Settle();

            Named<Button>(letter, "send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(
                () => !open.GetVisualDescendants().OfType<ModalOverlay>().Any() || Named<TextBlock>(letter, "letterStatus").IsVisible,
                () => $"the letter to be sent or refused. {Situation(open)}");

            return true;
        });

    /// <summary>What a failure says of the window: everything the report line has said, and any question up over it.</summary>
    private static string Situation(MainWindow open)
    {
        var said = open.GetVisualDescendants().OfType<ReportLine>().SingleOrDefault()?.History ?? [];
        var asking = open.GetVisualDescendants().OfType<ModalOverlay>()
            .Select(dialog => string.Join(" ", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)))
            .ToList();

        return $"The report line said: {(said.Count == 0 ? "nothing" : string.Join(" | ", said).TrimEnd('.'))}. "
            + (asking.Count == 0 ? "Nothing is up over the window." : $"Up over the window: {string.Join(" / ", asking)}");
    }

    /// <summary>
    /// The one <typeparamref name="T"/> named <paramref name="name"/> under <paramref name="within"/>,
    /// or a failure naming the ones there are.
    /// </summary>
    private static T Named<T>(Visual within, string name) where T : Control
    {
        var all = within.GetVisualDescendants().OfType<T>().ToList();
        var named = all.Where(control => control.Name == name).ToList();

        if (named.Count == 1) return named[0];

        // Less the parts of a control's own template, which no step looks for.
        var others = all.Select(control => control.Name).OfType<string>()
            .Where(other => !other.StartsWith("PART_", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal);
        var where = within is MainWindow open ? " " + Situation(open) : string.Empty;

        throw new InvalidOperationException(named.Count == 0
            ? $"No {typeof(T).Name} named “{name}”; the named ones are: {string.Join(", ", others)}.{where}"
            : $"{named.Count} of {typeof(T).Name} are named “{name}”, where one was looked for.{where}");
    }

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

    /// <summary>Opens a shared preset's file as a page sent one by <c>?file=</c> does, and answers whether it opened.</summary>
    public bool OpenShared(string name, string fileName, byte[] bytes) =>
        Run(() => Service<Flyback.Editor.Gallery.PresetSlot>().OpenSharedAsync(name, fileName, bytes));

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
        Named<AvaloniaEdit.TextEditor>(open, "source");

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
        var panel = Named<StackPanel>(window, "inspector");

        if (panel.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == caption) is not { } label)
            return null;

        Control row = label;
        while (row.GetVisualParent() is Control parent && parent != panel) row = parent;

        return (label, row);
    }

    private static Button? RowButton(Control row, string name) =>
        row.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == name);

    private static SeekTrack SeekBar(MainWindow window) =>
        Named<SeekTrack>(window, "seek");

    private static ComboBox Presets(MainWindow window) =>
        Named<ComboBox>(window, "presets");

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
