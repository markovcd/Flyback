using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Flyback.Editor.Files;
using Flyback.Editor.Statistics;
using Flyback.Editor.Desktop.Updates;
using Flyback.Editor.Updates;
using Flyback.Editor.Windows;
using Flyback.Ui;
using Flyback.Core;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Desktop;

public sealed class FlybackApp : Application
{
    public override void Initialize() => EditorTheme.Apply(this);

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
            new Launch(First: Startup.FirstRun, Updated: Startup.Updated, File: Startup.OpenPath is not null));

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

        var setup = EditorSetup.ThisMachine(usage) with
        {
            Launch = new EditorLaunch
            {
                OpenPath = Startup.OpenPath,
                OpenShared = Startup.OpenShared,
                Interpreted = Startup.Interpreted,
                OpeningNote = Startup.OpeningNote,
                WhatsNew = Startup.WhatsNew,
            },
            Plugins = Startup.Plugins,
        };
        var provider = EditorServices.Provider(setup);
        var window = provider.Window();
        window.Start();
        desktop.MainWindow = window;

        // Once there is a window, so a slow network is never a slow start.
        Updater.CheckInBackground(Startup.Updates);

        // Windows and Linux hand a file to open in through argv, which
        // Startup.OpenPath already carries — see Program.Main. macOS never
        // does: Finder delivers "open this file" as an activation instead,
        // whether it is what launches the program or a file dropped on its
        // Dock icon while it is already running, and there is no other way
        // to hear about either. The desktop lifetime does not implement it;
        // the application hands it out as a feature.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += async (_, e) =>
            {
                if (e is not FileActivatedEventArgs { Files: [IStorageFile file, ..] }) return;

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
