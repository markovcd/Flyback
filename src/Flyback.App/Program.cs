using System.Diagnostics;
using Avalonia;

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

        Startup.Load();
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
