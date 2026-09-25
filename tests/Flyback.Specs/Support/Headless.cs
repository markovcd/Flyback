using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Flyback.App;

namespace Flyback.Specs.Support;

/// <summary>
/// The one headless UI thread every window in the specs runs on, the editor's and the
/// viewer's, started the first time a step needs it.
/// </summary>
internal static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(EditorApp), AvaloniaTestIsolationLevel.PerAssembly));

    public static void Run(Action act) => Session.Value.Dispatch(act, CancellationToken.None).GetAwaiter().GetResult();

    public static T Run<T>(Func<T> act) => Session.Value.Dispatch(act, CancellationToken.None).GetAwaiter().GetResult();

    public static T Run<T>(Func<Task<T>> act) => Session.Value.Dispatch(act, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>
/// One scenario's hold on the headless platform, from its first window to its last.
/// </summary>
/// <remarks>
/// The platform has one keyboard, one clipboard and one UI thread for the whole run, so
/// scenarios that open windows take turns: between two of one scenario's steps, another's
/// could take the keyboard focus, copy over what it put on the clipboard, or hold back the
/// timers its clock runs on.
/// </remarks>
public sealed class HeadlessTurn
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly HashSet<object> holders = [];

    /// <summary>Waits for the platform, unless this scenario holds it already.</summary>
    public void Take(object holder)
    {
        if (holders.Count == 0) Gate.Wait();

        holders.Add(holder);
    }

    /// <summary>Lets the platform go once <paramref name="holder"/> was the last window left open.</summary>
    public void Leave(object holder)
    {
        if (holders.Remove(holder) && holders.Count == 0) Gate.Release();
    }
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
