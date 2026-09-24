using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.App.Midi;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Viewer;

/// <summary>What one run plays: the patch, the device it plays through, what it is played from, and how.</summary>
internal sealed record ViewerLaunch(
    Opened Opened,
    IAudioDevice? Device,
    ViewerOptions Options,
    IMidiInput? Instruments = null,
    Takeover Takeover = Takeover.Jump)
{
    /// <summary>Whether there is a picture to show: one the patch draws, in a window, on a run that wants video.</summary>
    public bool Pictured => !Options.Hidden && Options.Video && Opened.Patch.Reaches().Picture;
}

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

                var player = ViewerServices.Player(launch);

                player.Finished += () => desktop.Shutdown();
                desktop.Exit += (_, _) => player.Dispose();

                Dispatcher.UIThread.Post(player.Begin);
            }
            else
            {
                desktop.MainWindow = ViewerServices.Window(launch);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
