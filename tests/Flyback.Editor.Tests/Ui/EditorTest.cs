using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Site;
using Flyback.Editor.Tests.Ui;
using Flyback.Editor.Windows;
using Flyback.Ui.Testing;
using Flyback.Ui.Testing.Headless;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// Every [AvaloniaFact] and [AvaloniaTheory] in this assembly runs against this
// application, on a UI thread the session owns. Declared once, at the assembly.
[assembly: AvaloniaTestApplication(typeof(EditorTest))]

// xunit 4 runs every test in parallel by default, regardless of collection. The
// UI ones all queue on the one thread headless gives the assembly, so that buys
// nothing and only puts more of them in the queue at once. This is what xunit 3
// did, and what the timings here were measured against.
[assembly: Parallelization(Mode = ParallelMode.Collections)]

// Enough pool threads that the tests waiting on the UI thread cannot use them all up.
[assembly: AssemblyFixture(typeof(PoolHeadroom))]

namespace Flyback.Editor.Tests.Ui;

/// <summary>A <see cref="UiTest"/> that can open the editor: its canvas, its window and its container.</summary>
public class EditorTest : UiTest
{
    /// <summary>Where a module's parts sit on a canvas drawn in full, as every test's window starts.</summary>
    internal static NodeGeometry Geometry { get; } = new();

    private readonly List<ServiceProvider> providers = [];
    private readonly Dictionary<MainWindow, IServiceProvider> editorContainers = [];

    public static AppBuilder BuildAvaloniaApp() => HeadlessApp.Build<TestApp>();

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

    protected override void Close(Window window)
    {
        // A shell asks about unsaved work and cancels the close to do it,
        // which nothing here would answer.
        if (window is MainWindow shell) shell.CloseWithoutAsking();
        else base.Close(window);
    }

    public override void Dispose()
    {
        base.Dispose();

        // After the windows, which close over the containers that built them.
        foreach (var provider in providers) provider.Dispose();

        providers.Clear();

        GC.SuppressFinalize(this);
    }

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

    /// <summary>Turns the settings window to the tab headed <paramref name="name"/>.</summary>
    /// <remarks>By name rather than by row, because a row names one tab until the tabs are reordered.</remarks>
    protected static void ShowSettingsTab(Visual dialog, string name)
    {
        var tabs = All<TabControl>(dialog).Single(t => t.Name == "settingsTabs");

        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => (item.Header as TextBlock)?.Text == name);
    }
}
