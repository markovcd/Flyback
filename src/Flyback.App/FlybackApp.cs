using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Flyback.App.Statistics;
using Flyback.App.Updates;

namespace Flyback.App;

public sealed class FlybackApp : Application
{
    /// <summary>
    /// The code editor's own styles, which come out of its package rather than from
    /// anything here.
    /// </summary>
    /// <remarks>
    /// Here rather than in two places, because the test application needs the same
    /// ones — without them the editor is an unstyled shell. Loaded in C# rather than
    /// declared in markup, which is ADR-0016's rule holding even for somebody else's
    /// XAML. A new one each time rather than one shared: a style belongs to exactly
    /// one collection, and the headless session builds a fresh application per test.
    /// </remarks>
    public static IStyle EditorStyles() =>
        new StyleInclude(new Uri("avares://Flyback.App/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        };

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(EditorStyles());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before the window, because the window says what it started as as
            // soon as it has asked for a sound device (ADR-0094).
            var usage = Usage.Start(
                UsageSettings.Load(UsageSettings.File),
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

            var window = new MainWindow(
                openPath: Startup.OpenPath,
                outputSettingsPath: OutputSettings.File,
                updateSettingsPath: UpdateSettings.File,
                interpreted: Startup.Interpreted,
                updateNote: Startup.UpdateNote,
                whatsNew: Startup.WhatsNew,
                usageSettingsPath: UsageSettings.File,
                usage: usage,
                recoveryFolder: Recovery.Folder);
            desktop.MainWindow = window;

            // Once there is a window, so a slow network is never a slow start.
            Updater.CheckInBackground(Startup.Updates);

            // Windows and Linux hand a file to open in through argv, which
            // Startup.OpenPath already carries — see Program.Main. macOS never
            // does: Finder delivers "open this file" as an activation instead,
            // whether it is what launches the program or a file dropped on its
            // Dock icon while it is already running, and there is no other way
            // to hear about either.
            if (desktop is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs { Files: [var first, ..] }
                        && first is IStorageFile file)
                    {
                        _ = window.OpenActivatedFileAsync(file);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
