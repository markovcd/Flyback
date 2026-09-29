using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Flyback.App;

/// <summary>What the editor looks like, for every program that shows it and the tests that build one.</summary>
internal static class EditorTheme
{
    /// <summary>Fluent, dark, and the code editor's own styles.</summary>
    /// <remarks>
    /// The code editor's styles come out of its package, loaded in C# rather than
    /// declared in markup (ADR-0016). A new one each time: a style belongs to exactly
    /// one collection, and the headless session builds a fresh application per test.
    /// </remarks>
    public static void Apply(Application application)
    {
        application.Styles.Add(new FluentTheme());
        application.Styles.Add(new StyleInclude(new Uri("avares://Flyback.Editor/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        application.RequestedThemeVariant = ThemeVariant.Dark;
    }
}
