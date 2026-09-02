using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Flyback.App;

public sealed class FlybackApp : Application
{
    /// <summary>
    /// The code editor's own styles, which come out of its package rather than
    /// from anything here.
    /// </summary>
    /// <remarks>
    /// Written down here rather than in two places, because the test
    /// application needs the same ones: without them the editor is an unstyled
    /// shell and a test would be looking at a control nobody has. Loaded in C#
    /// rather than declared in markup, which is ADR-0016's rule holding even
    /// where what is being included is somebody else's XAML.
    /// <para>
    /// A new one each time rather than one shared. A style belongs to exactly
    /// one collection — a second owner is an exception, not a second reference —
    /// and the headless test session builds a fresh application per test.
    /// </para>
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
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
