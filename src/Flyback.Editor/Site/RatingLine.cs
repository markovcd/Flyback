using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Flyback.App.Controls;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Site;

/// <summary>A shared preset's or plugin's stars, to read: they are given on the site, never here.</summary>
internal static class RatingLine
{
    private static readonly IBrush Lit = new SolidColorBrush(Colors.Attention);

    private static readonly IBrush Unlit = new SolidColorBrush(Colors.Separator);

    public static TextBlock Of(SiteRating rating)
    {
        var line = new TextBlock { Name = "siteRating", FontSize = Text.Caption, Foreground = Text.Muted };

        line.Inlines!.Add(new Run(new string('★', rating.Stars)) { Foreground = Lit });
        line.Inlines.Add(new Run(new string('★', SiteRating.Most - rating.Stars)) { Foreground = Unlit });
        line.Inlines.Add(new Run("  " + rating.Said));

        ToolTip.SetTip(line, "Rated on the preset site. Rate it on its page there.");

        return line;
    }
}
