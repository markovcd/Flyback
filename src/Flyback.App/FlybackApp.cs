using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

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
            var window = new MainWindow(
                openPath: Startup.OpenPath,
                outputSettingsPath: OutputSettings.File,
                interpreted: Startup.Interpreted);
            desktop.MainWindow = window;

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
