using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.Core;
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

        Content = new Image { Source = new Bitmap(banner), Stretch = Stretch.UniformToFill };
    }
}
