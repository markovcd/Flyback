using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Site;

/// <summary>A line of text that opens an address in the system browser.</summary>
internal static class SiteLink
{
    public static TextBlock To(string text, Uri uri)
    {
        var link = new TextBlock
        {
            Text = text,
            FontSize = Text.Caption,
            Foreground = new SolidColorBrush(Colors.Attention),
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        link.PointerPressed += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(link)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(uri);
        };

        return link;
    }
}
