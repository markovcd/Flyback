using Avalonia;
using Avalonia.Headless;

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
