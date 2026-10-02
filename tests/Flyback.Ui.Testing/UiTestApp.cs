using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Flyback.Ui.Testing;

/// <summary>Nothing but the dark Fluent theme, for a test of a control that needs no more.</summary>
public sealed class UiTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}
