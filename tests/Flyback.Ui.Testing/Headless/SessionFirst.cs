using System.Reflection;
using Avalonia.Headless;

namespace Flyback.Ui.Testing.Headless;

/// <summary>
/// Starts the assembly's headless session before any test runs, so the UI thread
/// is the session's.
/// </summary>
/// <remarks>
/// <c>Dispatcher.UIThread</c> belongs to whichever thread touches it first. A plain
/// test that asks it <c>CheckAccess()</c> from a pool thread, ahead of the first
/// <c>[AvaloniaFact]</c>, gives it that thread, and the application then cannot be
/// built on the session's: the compositor refuses from "a different thread". An
/// assembly that runs UI tests beside plain ones names it:
/// <c>[assembly: AssemblyFixture(typeof(SessionFirst))]</c>.
/// </remarks>
public sealed class SessionFirst
{
    public SessionFirst()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(TestAssembly());

        // The application is built on the first dispatch, not on the start.
        session.Dispatch(() => { }, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>The assembly that names its application, which is the one being run.</summary>
    private static Assembly TestAssembly() =>
        new[] { Assembly.GetEntryAssembly() }
            .Concat(AppDomain.CurrentDomain.GetAssemblies())
            .OfType<Assembly>()
            .First(assembly => assembly.GetCustomAttribute<AvaloniaTestApplicationAttribute>() is not null);
}
