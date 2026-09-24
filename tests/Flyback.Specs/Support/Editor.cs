using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App;
using Flyback.App.Controls;
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
/// against what is on the canvas.
/// </remarks>
public sealed class Editor(PatchContext context) : IDisposable
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(EditorApp), AvaloniaTestIsolationLevel.PerAssembly));

    /// <summary>How many turns the UI thread is given after a step, enough for a clipboard's round trip.</summary>
    private const int Turns = 4;

    private MainWindow? window;

    private readonly List<string> said = [];

    /// <summary>Everything the canvas has said since the window opened.</summary>
    public IReadOnlyList<string> Said => said;

    /// <summary>What is selected on the canvas, as the editor holds it now.</summary>
    internal IReadOnlyList<NodeInstance> Selected => Read(canvas => canvas.Selection.Nodes);

    /// <summary>Whether the title says there is work to lose.</summary>
    public bool Unsaved => Read(_ => window!.Title?.EndsWith('•') == true);

    /// <summary>Whether the toolbar's undo has anything to take back.</summary>
    public bool CanUndo => Read(canvas => canvas.History.CanUndo);

    /// <summary>Opens the window on the scenario's patch, if it is not open already.</summary>
    public void Open() => Do(_ => { });

    /// <summary>Makes the selection exactly these modules, which is what clicking them with Ctrl held does.</summary>
    public void Select(params Guid[] ids) => Do(canvas => canvas.Selection.Take(ids));

    /// <summary>Presses a key, with Ctrl or anything else held, while the canvas has the focus.</summary>
    public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        Do(canvas =>
        {
            var open = window!;

            canvas.Focus();
            open.KeyPressQwerty(key, modifiers);
            open.KeyReleaseQwerty(key, modifiers);
        });

    /// <summary>Presses a key with Ctrl held, as every editing shortcut is.</summary>
    public void PressCtrl(PhysicalKey key, bool shift = false) =>
        Press(key, RawInputModifiers.Control | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));

    /// <summary>Runs what a step does to the canvas, where no key does it: a panel's button, say.</summary>
    internal void Do(Action<NodeEditor> act) =>
        Run(async () =>
        {
            act(Canvas());

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

    public void Dispose()
    {
        if (window is not { } open) return;

        Run(() =>
        {
            open.CloseWithoutAsking();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>The canvas, opening the window on the scenario's patch the first time.</summary>
    private NodeEditor Canvas()
    {
        if (window is null)
        {
            window = EditorServices.Window();
            window.Show();
            Settle();

            CanvasIn(window).Report.Said += (_, line) => said.Add(line);
            CanvasIn(window).History.Open(context.Patch);
            CanvasIn(window).Focus();
            Settle();
        }

        return CanvasIn(window);
    }

    private static NodeEditor CanvasIn(MainWindow window) =>
        window.GetVisualDescendants().OfType<NodeEditor>().Single();

    private void Settle()
    {
        window!.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Run(Action act) => Session.Value.Dispatch(act, CancellationToken.None).GetAwaiter().GetResult();

    private static T Run<T>(Func<T> act) => Session.Value.Dispatch(act, CancellationToken.None).GetAwaiter().GetResult();

    private static T Run<T>(Func<Task<T>> act) => Session.Value.Dispatch(act, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>The theme the editor runs in, and nothing else of the program's own application.</summary>
public sealed class EditorApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<EditorApp>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(FlybackApp.EditorStyles());
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}
