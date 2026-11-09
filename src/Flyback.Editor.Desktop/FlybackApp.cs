using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.Assist;
using Flyback.Assist.Decide;
using Flyback.Core;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Desktop.Updates;
using Flyback.Editor.Files;
using Flyback.Editor.Shell;
using Flyback.Editor.Statistics;
using Flyback.Editor.Windows;
using Flyback.Plugins.Hosting;
using Flyback.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Desktop;

public sealed class FlybackApp : Application
{
    private readonly DesktopLaunch launch;

    private readonly AppMenu menu = new();

    internal FlybackApp(DesktopLaunch launch) => this.launch = launch;

    public override void Initialize()
    {
        // Before Avalonia's macOS setup, which reads both into the menu bar.
        Name = GlobalConstants.ApplicationName;
        NativeMenu.SetMenu(this, menu.Menu);
        EditorTheme.Apply(this, StartingScheme.For(ThisMachine.Folders()));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new NotSupportedException("Flyback app is not initialized.");
        }

        if (StallTrace.On)
        {
            var watch = new StallWatch();

            desktop.Exit += (_, _) => watch.Dispose();
        }

        // Before the window, because the window says what it started as as
        // soon as it has asked for a sound device (ADR-0094).
        var usage = Usage.Start(
            UsageSettings.Load(SettingsFile.Path),
            new Launch(First: launch.FirstRun, Updated: launch.Updated, File: launch.Editor.OpenPath is not null));

        // A crash is said with the little that may be said about it, and the
        // process kept for as long as that takes and no longer (ADR-0103).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;

            usage.Crashed(ex);
            usage.Drain(Usage.LongestWait);
        };

        // The end of the run is where what it did is added up, and the only
        // moment anything waits for a statistic to arrive.
        desktop.Exit += (_, _) =>
        {
            usage.Ended();
            usage.Drain(Usage.LongestWait);
        };

        var shown = new TaskCompletionSource<(ServiceProvider Provider, MainWindow Window)>();

        // Windows and Linux hand a file to open in through argv, which
        // the launch's OpenPath already carries — see Program.Main. macOS never
        // does: Finder delivers "open this file" as an activation instead,
        // whether it is what launches the program or a file dropped on its
        // Dock icon while it is already running, and there is no other way
        // to hear about either. The desktop lifetime does not implement it;
        // the application hands it out as a feature. Heard from the start and
        // acted on once the window is up, which a splash puts off.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += async (_, e) =>
            {
                if (e is not FileActivatedEventArgs { Files: [IStorageFile file, ..] }) return;

                var (provider, window) = await shown.Task;

                if (OperatingSystem.IsMacOS()
                    && FileTypeSettings.Load(SettingsFile.Path).Opener == FileOpener.Viewer
                    && !PluginPackage.Named(file.Name)
                    && file.TryGetLocalPath() is { } path)
                {
                    PassToViewer(path, desktop, window);
                    return;
                }

                await provider.GetRequiredService<PatchOpening>().OpenActivatedFileAsync(file);
            };
        }

        var untilReady = SlowStart.SinceLaunch();
        var lastWindow = SlowStart.Last(SlowStart.File);

        if (!launch.Splash && !SlowStart.Splashes(untilReady, lastWindow))
        {
            shown.SetResult(Open(desktop, usage));
            return;
        }

        Trace.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"startup: splash, as Avalonia was ready after {untilReady.TotalSeconds:0.00} s and the last window took {lastWindow?.TotalSeconds.ToString("0.00 s", CultureInfo.InvariantCulture) ?? "unknown"}"));

        var splash = new SplashWindow();

        splash.Show();

        // Below the splash's first frame, which then stays up while the window is built.
        Dispatcher.UIThread.Post(
            () =>
            {
                var opened = Open(desktop, usage);

                opened.Window.Opened += (_, _) => splash.Close();
                opened.Window.Show();
                shown.SetResult(opened);
            },
            DispatcherPriority.Background);
    }

    /// <summary>Builds the editor's window and makes it the main one; the lifetime shows it, or the caller does once it is running.</summary>
    private (ServiceProvider Provider, MainWindow Window) Open(IClassicDesktopStyleApplicationLifetime desktop, Usage usage)
    {
        var setup = ThisMachine.Setup(usage) with { Launch = launch.Editor, Plugins = launch.Plugins, Assistants = Assistants.Shipped, DecisionModels = DecisionModels.Shipped };
        var provider = EditorServices.Provider(setup);
        menu.Serve(provider);
        var window = provider.Window();
        window.Start();
        window.Opened += (_, _) => SlowStart.Remember(SlowStart.File, SlowStart.SinceLaunch());
        desktop.MainWindow = window;

        // Once there is a window, so a slow network is never a slow start.
        Updater.CheckInBackground(launch.Updates);

        return (provider, window);
    }

    /// <summary>
    /// Finder can only hand a file to the bundle, so the editor starts the viewer
    /// with it (ADR-0127).
    /// </summary>
    /// <remarks>
    /// An editor that was only started to receive the file closes again. Finder
    /// sends the file within moments of the launch, and there is no other sign of
    /// which launch it was.
    /// </remarks>
    private static void PassToViewer(string path, IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        var viewer = Path.Combine(AppContext.BaseDirectory, FileTypes.ViewerName);

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(viewer) { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception ex)
        {
            window.Report($"The viewer did not start: {ex.Message}", viewer);
            return;
        }

        if (DateTime.Now - Process.GetCurrentProcess().StartTime < LaunchedForAFile && window.HoldsNoWork)
            desktop.Shutdown();
    }

    private static readonly TimeSpan LaunchedForAFile = TimeSpan.FromSeconds(5);
}
