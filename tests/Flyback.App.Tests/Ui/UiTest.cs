using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit.Sdk;
using Xunit.v3;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Flyback.App.Canvas;
using Flyback.App.Site;
using Flyback.App.Tests.Ui;
using Flyback.App.Windows;

// Every [AvaloniaFact] and [AvaloniaTheory] in this assembly runs against this
// application, on a UI thread the session owns. Declared once, at the assembly.
[assembly: AvaloniaTestApplication(typeof(UiTest))]

// xunit 4 runs every test in parallel by default, regardless of collection. The
// UI ones all queue on the one thread headless gives the assembly, so that buys
// nothing and only puts more of them in the queue at once. This is what xunit 3
// did, and what the timings here were measured against.
[assembly: Parallelization(Mode = ParallelMode.Collections)]

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Avalonia application a UI test runs inside, and the small amount of
/// scaffolding one needs.
/// </summary>
/// <remarks>
/// Headless is a real Avalonia: it measures, arranges, applies templates, routes
/// input and — with Skia underneath — rasterises. What it does not do is open a
/// window. The Fluent theme is not decoration: every templated control the
/// inspector uses is an empty shell without it.
/// <para>
/// Every test in a class deriving from this one is an <c>[AvaloniaFact]</c> or an
/// <c>[AvaloniaTheory]</c>, whether it touches a control or not. A plain
/// <c>[Fact]</c> runs on a thread of the pool's choosing, and disposing there
/// reaches the dispatcher from the wrong thread — which throws only when there
/// happens to be work queued, so it shows up as another test failing, later.
/// </para>
/// </remarks>
public class UiTest : IDisposable
{
    /// <summary>Where a module's parts sit on a canvas drawn in full, as every test's window starts.</summary>
    internal static NodeGeometry Geometry { get; } = new();

    /// <summary>
    /// The windows this test opened, closed when it ends. Headless runs the whole
    /// assembly on one UI thread, so a window left open keeps its preview, its
    /// timers and its engine on that thread for every test that follows.
    /// </summary>
    private readonly List<Window> opened = [];
    private readonly List<ServiceProvider> providers = [];
    private readonly Dictionary<MainWindow, IServiceProvider> editorContainers = [];

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseSkia()

        // The same font the program ships with, so a test that looks at what
        // was drawn is looking at what a user would see. It is also the only
        // way to find out here whether a glyph the shell asks for exists —
        // a missing one is a box on a button rather than a failure anywhere.
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>
    /// Puts a control in a window and lays it out, which is what makes its
    /// templates real. Everything below the window is the control under test.
    /// </summary>
    /// <param name="content">The control under test, which becomes the whole of the window.</param>
    /// <param name="width">
    /// The inspector's own minimum, so a test sees the layout at the narrowest
    /// the panel is allowed to be rather than at whatever a window happened to be.
    /// </param>
    protected Window Show(Control content, double width = 300)
    {
        var window = Owned(new Window
        {
            Width = width,
            SizeToContent = SizeToContent.Height,
            Content = content,
        });

        window.Show();
        Settle(window);

        return window;
    }

    /// <summary>
    /// A canvas built the way the editor builds one, from its container, at the size
    /// given. <paramref name="replace"/> swaps any of its services for a test's own.
    /// </summary>
    internal static NodeEditor NewCanvas(double width, double height, Action<IServiceCollection>? replace = null)
    {
        var canvas = EditorServices.CanvasProvider(replace).GetRequiredService<NodeEditor>();

        canvas.Width = width;
        canvas.Height = height;

        return canvas;
    }

    /// <summary>
    /// A window this test owns, and which is closed with it, built by the editor's
    /// container. <paramref name="replace"/> swaps any of its services for a test's own.
    /// </summary>
    internal MainWindow NewMainWindow(EditorSetup? setup = null, Action<IServiceCollection>? replace = null)
    {
        var provider = Container(setup, replace, validate: true);
        var window = Owned(provider.Window());
        editorContainers.Add(window, provider);
        window.Start();

        return window;
    }

    internal T Service<T>(MainWindow window) where T : notnull =>
        editorContainers[window].GetRequiredService<T>();

    /// <summary>
    /// A service from the editor's own container, with its graph built as the editor
    /// builds it and nothing started: no window exists until one is resolved and
    /// started. <paramref name="replace"/> swaps any of its services for a test's own.
    /// </summary>
    internal T Resolve<T>(EditorSetup? setup = null, Action<IServiceCollection>? replace = null) where T : notnull =>
        Container(setup, replace).GetRequiredService<T>();

    /// <summary>The editor's container itself, for a test that takes several services from one window's graph.</summary>
    internal ServiceProvider Container(EditorSetup? setup = null, Action<IServiceCollection>? replace = null, bool validate = false)
    {
        var provider = EditorServices.Provider(setup, replace, validate);
        providers.Add(provider);

        return provider;
    }

    /// <summary><paramref name="window"/> is the one <paramref name="container"/>'s window-bound services act on.</summary>
    internal static void Attach(IServiceProvider container, Window window)
    {
        container.GetRequiredService<WindowHolder>().Attach(window);
    }

    /// <summary>Asks the preset site through <paramref name="site"/> rather than over the network.</summary>
    internal static Action<IServiceCollection> Site(HttpMessageHandler site) =>
        // Kept for the whole test, so the factory never retires and disposes it.
        services => services.AddHttpClient(SiteAccess.Client)
            .ConfigurePrimaryHttpMessageHandler(() => site)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

    /// <summary>A window of the editor this test owns, shown and laid out, on <paramref name="patch"/> where one is given.</summary>
    internal MainWindow Open(Patch? patch = null, EditorSetup? setup = null, Action<IServiceCollection>? replace = null)
    {
        var window = NewMainWindow(setup, replace);

        window.Show();
        Settle(window);

        if (patch is null) return window;

        Editor(window).History.Open(patch);
        Settle(window);

        return window;
    }

    /// <summary>The canvas in an editor's window.</summary>
    internal static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    /// <summary>
    /// A canvas on its own, 1200 by 800, in a window of its own and showing
    /// <paramref name="patch"/>, for a gesture that needs nothing else of the editor.
    /// </summary>
    internal (NodeEditor Editor, Window Window) Editing(Patch patch, Action<IServiceCollection>? replace = null) =>
        Editing(patch, 1200, 800, replace);

    /// <summary>The same, at a size of the test's own.</summary>
    internal (NodeEditor Editor, Window Window) Editing(Patch patch, double width, double height, Action<IServiceCollection>? replace = null)
    {
        var editor = NewCanvas(width, height, replace);
        var window = Show(editor, width);

        editor.History.Open(patch);
        Settle(window);

        return (editor, window);
    }

    /// <summary>The middle of a module's title bar: somewhere on it no socket is.</summary>
    internal static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    /// <summary>Where a point of the patch is on the window, for the pointer to be put there.</summary>
    internal static Point Screen(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    /// <summary>Clicks the left button at a point of the patch, <paramref name="count"/> times over for a double-click.</summary>
    internal static void Click(
        NodeEditor editor,
        Window window,
        Point graph,
        RawInputModifiers modifiers = RawInputModifiers.None,
        int count = 1)
    {
        var at = Screen(editor, window, graph);

        for (var i = 0; i < count; i++)
        {
            window.MouseDown(at, MouseButton.Left, modifiers);
            window.MouseUp(at, MouseButton.Left, modifiers);
        }

        Settle(window);
    }

    /// <summary>Clicks a module's title bar.</summary>
    internal static void Click(NodeEditor editor, Window window, NodeInstance node, RawInputModifiers modifiers = RawInputModifiers.None) =>
        Click(editor, window, Body(node), modifiers);

    /// <summary>Drags with the left button from one point of the patch to another, in one move.</summary>
    internal static void Drag(
        NodeEditor editor, Window window, Point fromGraph, Point toGraph, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var from = Screen(editor, window, fromGraph);
        var to = Screen(editor, window, toGraph);

        window.MouseDown(from, MouseButton.Left, modifiers);
        window.MouseMove(to, modifiers);
        window.MouseUp(to, MouseButton.Left, modifiers);
        Settle(window);
    }

    /// <summary>The type ids of what is selected, in order, so a selection reads the same way twice.</summary>
    internal static string[] Selected(NodeEditor editor) =>
        [.. editor.Selection.Nodes.Select(n => n.TypeId).Order()];

    /// <summary>Presses a button the way a click would, without a pointer.</summary>
    internal static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Hands a window this test made over to be closed when it ends.</summary>
    protected T Owned<T>(T window) where T : Window
    {
        opened.Add(window);

        return window;
    }

    public virtual void Dispose()
    {
        // In reverse, so a window opened over another goes first.
        for (var index = opened.Count - 1; index >= 0; index--)
        {
            var window = opened[index];

            // A shell asks about unsaved work and cancels the close to do it,
            // which nothing here would answer.
            if (window is MainWindow shell) shell.CloseWithoutAsking();
            else window.Close();
        }

        opened.Clear();

        // After the windows, which close over the containers that built them.
        foreach (var provider in providers) provider.Dispose();

        providers.Clear();

        Dispatcher.UIThread.RunJobs();

        GC.SuppressFinalize(this);
    }

    /// <summary>Runs layout to completion, after something has changed the tree.</summary>
    protected static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>
    /// Runs the dispatcher until <paramref name="until"/> holds, for work that finishes on the
    /// thread pool. Lays out <paramref name="window"/> as it goes, where a list builds its rows in layout.
    /// </summary>
    /// <remarks>
    /// The deadline only catches a hang: a loaded machine runs a quarter-second debounce and
    /// a pool hop many times slower, and a wait that gives up quietly fails an assertion later.
    /// </remarks>
    protected static void Pump(
        Func<bool> until,
        Window? window = null,
        [CallerArgumentExpression(nameof(until))] string waitingFor = "")
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (!until())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Waited 30 s for {waitingFor}");

            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }

    /// <summary>Every descendant of a control, itself included.</summary>
    protected static IEnumerable<Visual> Tree(Visual root)
    {
        yield return root;

        foreach (var child in root.GetVisualChildren())
        foreach (var node in Tree(child))
            yield return node;
    }

    protected static IEnumerable<T> All<T>(Visual root) where T : Visual => Tree(root).OfType<T>();

    /// <summary>Every knob's slider in the window, which the toolbar's seek bar and Volume are not.</summary>
    protected static IEnumerable<Slider> Knobs(Visual root) => All<Slider>(root).Where(slider => slider.Name is not ("seek" or "volume"));

    /// <summary>Picks the preset called <paramref name="name"/> out of a preset list.</summary>
    /// <remarks>
    /// By name rather than by row, because a row number names one preset until a
    /// plugin adds another ahead of it.
    /// </remarks>
    protected static void Pick(ComboBox presets, string name)
    {
        var rows = presets.ItemsSource!.Cast<object>().ToList();
        var row = rows.FindIndex(item => item is PatchPreset preset && preset.Name == name);

        row.ShouldBeGreaterThanOrEqualTo(0, $"the list offers no preset called {name}");

        presets.SelectedIndex = row;
    }
}

/// <summary>Nothing but the theme — the shell's own App does far more than a test wants.</summary>
/// <remarks>
/// The dark variant as well as the theme, because the program asks for it and a
/// test that looked at a light one would be looking at a window nobody has. It
/// is what decides the foreground of a button, so an icon drawn in its parent's
/// color comes out black here and light where it actually runs.
/// </remarks>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        // The editor's own, so the two cannot drift: without it the editor is an
        // unstyled shell and a test would be looking at a control nobody has.
        EditorTheme.Apply(this);
    }
}
