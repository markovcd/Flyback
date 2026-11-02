using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Flyback.Editor.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor;

/// <summary>What the editor looks like, for every program that shows it and the tests that build one.</summary>
internal static class EditorTheme
{
    /// <summary>Fluent, dark, in Flyback's own accent and corners, and the code editor's own styles.</summary>
    /// <remarks>
    /// The code editor's styles come out of its package, loaded in C# rather than
    /// declared in markup (ADR-0016). A new one each time: a style belongs to exactly
    /// one collection, and the headless session builds a fresh application per test.
    /// </remarks>
    public static void Apply(Application application)
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Colors.Attention };
        application.Styles.Add(fluent);

        var resources = application.Resources;
        resources["ControlCornerRadius"] = new CornerRadius(2);
        resources["OverlayCornerRadius"] = new CornerRadius(2);
        resources["ButtonBackgroundDisabled"] = Brushes.Transparent;
        resources["ToggleButtonBackgroundDisabled"] = Brushes.Transparent;

        // Dark ink on the amber of a toggle that is down; white on it fails contrast.
        var ink = new SolidColorBrush(Colors.Window);
        resources["ToggleButtonForegroundChecked"] = ink;
        resources["ToggleButtonForegroundCheckedPointerOver"] = ink;
        resources["ToggleButtonForegroundCheckedPressed"] = ink;

        application.Styles.Add(Bare(x => x.OfType<Button>().Class(ToolbarButtons.Tool)));
        application.Styles.Add(Bare(x => x.OfType<ToggleButton>().Class(ToolbarButtons.Tool)));
        application.Styles.Add(new StyleInclude(new Uri("avares://Flyback.Editor/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        application.RequestedThemeVariant = ThemeVariant.Dark;
    }

    /// <summary>No fill at rest, so a row of tool buttons reads as one bar; hover and a toggle that is down still show one.</summary>
    private static Style Bare(Func<Selector?, Selector> selector)
    {
        var style = new Style(selector);
        style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        return style;
    }
}
