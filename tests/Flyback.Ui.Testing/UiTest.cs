using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Flyback.Ui.Testing;

/// <summary>The scaffolding a test of a control needs, run inside a headless Avalonia.</summary>
/// <remarks>
/// Headless is a real Avalonia: it measures, arranges, applies templates, routes
/// input and, with Skia underneath, rasterizes. What it does not do is open a
/// window. The Fluent theme is not decoration: every templated control is an
/// empty shell without it.
/// <para>
/// Every test in a class deriving from this one is an <c>[AvaloniaFact]</c> or an
/// <c>[AvaloniaTheory]</c>, whether it touches a control or not. A plain
/// <c>[Fact]</c> runs on a thread of the pool's choosing, and disposing there
/// reaches the dispatcher from the wrong thread, which throws only when there
/// happens to be work queued, so it shows up as another test failing, later.
/// </para>
/// </remarks>
public abstract class UiTest : IDisposable
{
    /// <summary>
    /// The windows this test opened, closed when it ends. Headless runs the whole
    /// assembly on one UI thread, so a window left open keeps its preview, its
    /// timers and its engine on that thread for every test that follows.
    /// </summary>
    private readonly List<Window> opened = [];

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

    /// <summary>Hands a window this test made over to be closed when it ends.</summary>
    protected T Owned<T>(T window) where T : Window
    {
        opened.Add(window);

        return window;
    }

    /// <summary>Closes a window this test owns, for a kind that asks first.</summary>
    protected virtual void Close(Window window) => window.Close();

    public virtual void Dispose()
    {
        // In reverse, so a window opened over another goes first.
        for (var index = opened.Count - 1; index >= 0; index--) Close(opened[index]);

        opened.Clear();
        Dispatcher.UIThread.RunJobs();

        GC.SuppressFinalize(this);
    }

    /// <summary>Presses a button the way a click would, without a pointer.</summary>
    protected static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
}
