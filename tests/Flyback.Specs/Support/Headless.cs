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
