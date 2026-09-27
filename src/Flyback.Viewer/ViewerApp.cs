using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Flyback.Viewer;

public sealed class ViewerApp : Application
{
    /// <summary>Set by <c>Program</c> before Avalonia starts, since an application is built with no arguments.</summary>
    internal static ViewerLaunch? Launch { get; set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Launch is { } launch)
        {
            // A terminal's Ctrl+C closes the run the way its window's close button
            // would, which matters most where there is no window to close.
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
            };

            if (launch.Options.Hidden)
            {
                // No window is ever the main one, so nothing ends the run but --for
                // or Ctrl+C.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var run = ViewerServices.Player(launch);

                run.Player.Finished += () => desktop.Shutdown();
                desktop.Exit += (_, _) => run.Dispose();

                Dispatcher.UIThread.Post(run.Player.Begin);
            }
            else
            {
                desktop.MainWindow = ViewerServices.Window(launch);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
