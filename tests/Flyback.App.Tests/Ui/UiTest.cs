using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using Xunit.Sdk;
using Xunit.v3;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Flyback.App.Tests.Ui;

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
    /// <summary>
    /// The windows this test opened, closed when it ends. Headless runs the whole
    /// assembly on one UI thread, so a window left open keeps its preview, its
    /// timers and its engine on that thread for every test that follows.
    /// </summary>
    private readonly List<Window> opened = [];

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
        var services = new ServiceCollection().AddCanvas();
        replace?.Invoke(services);

        var canvas = services.BuildServiceProvider().GetRequiredService<NodeEditor>();

        canvas.Width = width;
        canvas.Height = height;

        return canvas;
    }

    /// <summary>
    /// A window this test owns, and which is closed with it, built by the editor's
    /// container. <paramref name="replace"/> swaps any of its services for a test's own.
    /// </summary>
    internal MainWindow NewMainWindow(EditorSetup? setup = null, Action<IServiceCollection>? replace = null) =>
        Owned(EditorServices.Window(setup, replace));

    /// <summary>Asks the preset site through <paramref name="site"/> rather than over the network.</summary>
    internal static Action<IServiceCollection> Site(HttpMessageHandler site) =>
        services => services.AddKeyedSingleton(SiteAccess.Client, new HttpClient(site));

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

    /// <summary>Every descendant of a control, itself included.</summary>
    protected static IEnumerable<Visual> Tree(Visual root)
    {
        yield return root;

        foreach (var child in root.GetVisualChildren())
        foreach (var node in Tree(child))
            yield return node;
    }

    protected static IEnumerable<T> All<T>(Visual root) where T : Visual => Tree(root).OfType<T>();

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
        Styles.Add(new FluentTheme());

        // The code editor's own, taken from the shell so the two cannot drift.
        // Without it the editor is an unstyled shell and a test would be looking
        // at a control nobody has — the same reason the Fluent theme is here.
        Styles.Add(FlybackApp.EditorStyles());

        RequestedThemeVariant = ThemeVariant.Dark;
    }
}
