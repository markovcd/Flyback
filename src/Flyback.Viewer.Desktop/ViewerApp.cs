using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Flyback.Core;
using Flyback.Ui;

namespace Flyback.Viewer.Desktop;

public sealed class ViewerApp : Application
{
    /// <summary>Set by <c>Program</c> before Avalonia starts, since an application is built with no arguments.</summary>
    internal static ViewerLaunch? Launch { get; set; }

    /// <summary>The name macOS shows in the menu bar.</summary>
    public const string Title = $"{GlobalConstants.ApplicationName} Viewer";

    private ViewerAbout? about;

    public override void Initialize()
    {
        // Before Avalonia's macOS setup, which reads both into the menu bar.
        Name = Title;
        NativeMenu.SetMenu(this, Menu());
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

            if (StallTrace.On)
            {
                var watch = new StallWatch();

                desktop.Exit += (_, _) => watch.Dispose();
            }

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

    /// <summary>The menu under the program's name; Avalonia adds Services, Hide and Quit below it.</summary>
    private NativeMenu Menu()
    {
        var item = new NativeMenuItem($"About {Title}");

        item.Click += (_, _) =>
        {
            if (about is null)
            {
                about = new ViewerAbout();
                about.Closed += (_, _) => about = null;
            }

            about.Show();
            about.Activate();
        };

        var menu = new NativeMenu();

        menu.Add(item);

        return menu;
    }
}
