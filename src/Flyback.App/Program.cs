using System.Diagnostics;
using Avalonia;
using Flyback.App.Updates;

namespace Flyback.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Only a console somebody is looking at is worth keeping or writing to.
        // Started from a shell, this is where the shell's own window is and
        // where LogToTrace below ends up; started from Explorer it is a window
        // Windows made because there was none to inherit, and it goes back.
        if (Terminal.Inherited)
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));

            // Written through rather than buffered. A trace listener holds what
            // it is given until something flushes it, and the two occasions this
            // is worth reading — watching a run, and looking at what a crash
            // said — are both occasions where nothing ever will.
            Trace.AutoFlush = true;
        }
        else
        {
            Terminal.Release();
        }

        // A launch that restarts one closing now waits for it first, before the
        // plugins it has open are looked at.
        args = Restart.Awaited(args);

        // A new version started by the old one to install itself, which is all
        // this launch is for — see Updater. It opens no window, and starts the
        // installed copy on its way out.
        if (Updater.Applying(args))
        {
            Updater.Apply(args);
            return;
        }

        // Before anything is loaded, because a version waiting to be installed
        // replaces the files loading would read — and this launch becomes that
        // version's, opened once it is in.
        var updates = UpdateSettings.Load(UpdateSettings.File);

        if (Updater.HandOff(args, updates)) return;

        // The one plain argument a launch can be given: a file dropped onto the
        // program's icon, or opened with it, arrives as the whole of args and
        // nothing else does — so anything that looks like a switch is left for
        // Avalonia's own lifetime to make of what it likes.
        Startup.Load(
            args.FirstOrDefault(a => !a.StartsWith('-')),
            interpreted: args.Contains(Startup.InterpretedFlag, StringComparer.OrdinalIgnoreCase),
            updates: updates);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <remarks>
    /// <see cref="AppBuilder.LogToTrace"/> is what makes the listener above
    /// worth adding: Avalonia's own complaints — a control that would not
    /// template, a renderer that would not start — go to <see cref="Trace"/>,
    /// which without a listener reaches a debugger and nothing else. With one
    /// they reach the terminal, which is the only place a person running the
    /// program from a terminal would think to look.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FlybackApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
