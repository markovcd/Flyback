using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Desktop;

/// <summary>The mark and the name, up while a slow launch builds its window (<see cref="SlowStart"/>).</summary>
internal sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        Title = GlobalConstants.ApplicationName;
        Width = 360;
        Height = 220;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Colors.Window);

        var starting = Text.Quiet("Starting…");

        starting.HorizontalAlignment = HorizontalAlignment.Center;

        Content = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new LogoMark { Width = 72, Height = 72 },
                new TextBlock
                {
                    Text = GlobalConstants.ApplicationName,
                    FontSize = Text.Display,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                starting,
            },
        };
    }
}
