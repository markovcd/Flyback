using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Flyback.Core;
using Flyback.Ui.Controls;

namespace Flyback.Viewer.Desktop;

/// <summary>The viewer's About window: what it is and which build.</summary>
internal sealed class ViewerAbout : Window
{
    /// <summary>The build's version, as the editor's About says it.</summary>
    public static string Version =>
        typeof(ViewerAbout).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public ViewerAbout()
    {
        Title = $"About {ViewerApp.Title}";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = ViewerApp.Title, FontSize = Text.Display, FontWeight = FontWeight.SemiBold },
                Text.Quiet($"Plays a {GlobalConstants.ApplicationName} patch.", Text.Body),
                Text.Quiet($"Version {Version}", Text.Body),
            },
        };
    }
}
