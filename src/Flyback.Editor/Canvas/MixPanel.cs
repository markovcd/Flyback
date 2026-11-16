using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Engine.Mix;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>What a mix check found, as the panel shows it: the figures, then each finding marked by how much it matters.</summary>
internal static class MixPanel
{
    public static Control View(MixReport report)
    {
        var page = new StackPanel { Name = "mixCheck", Spacing = 8, Width = 520, Margin = new Thickness(20, 12, 20, 20) };

        page.Children.Add(Line(MixWords.Heard(report), Text.Muted));

        if (report.Findings.Count == 0)
        {
            page.Children.Add(Line("Nothing to flag.", Text.Muted));
            return page;
        }

        var warning = new ImmutableSolidColorBrush(Colors.Fault);
        var notice = new ImmutableSolidColorBrush(Colors.Warning);

        foreach (var finding in report.Findings)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*") };
            var mark = new TextBlock
            {
                Text = "●",
                FontSize = Text.Small,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = finding.Severity == MixSeverity.Warning ? warning : notice,
            };
            var body = Line(finding.Text, null);

            ToolTip.SetTip(mark, finding.Severity == MixSeverity.Warning ? "Warning" : "Worth a look");
            Grid.SetColumn(body, 1);
            row.Children.Add(mark);
            row.Children.Add(body);
            page.Children.Add(row);
        }

        return page;
    }

    private static TextBlock Line(string text, IBrush? ink)
    {
        var block = new TextBlock { Text = text, FontSize = Text.Body, TextWrapping = TextWrapping.Wrap };

        if (ink is not null) block.Foreground = ink;

        return block;
    }
}
