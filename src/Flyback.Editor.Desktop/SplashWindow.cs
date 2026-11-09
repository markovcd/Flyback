using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.Core;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Desktop;

/// <summary>The banner, up while a slow launch builds its window (<see cref="SlowStart"/>).</summary>
internal sealed class SplashWindow : Window
{
    /// <summary>The site's <c>assets/splash.jpg</c>, which the web editor shows while it loads.</summary>
    public const string Banner = "splash.jpg";

    public SplashWindow()
    {
        Title = GlobalConstants.ApplicationName;
        Width = 640;
        Height = 360;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Colors.Window);

        using var banner = typeof(SplashWindow).Assembly.GetManifestResourceStream(Banner)!;

        Content = new Panel
        {
            Children =
            {
                new Image { Source = new Bitmap(banner), Stretch = Stretch.UniformToFill },
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 28),
                    Padding = new Thickness(14, 6),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Colors.Window, 0.9),
                    Child = Text.Quiet("Starting…"),
                },
            },
        };
    }
}
