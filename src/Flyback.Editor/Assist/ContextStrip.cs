using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Assist;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Assist;

/// <summary>
/// How full the conversation's context is, as a bar, opening onto what it has cost
/// in tokens. Hidden until a request reports one.
/// </summary>
internal sealed class ContextStrip : StackPanel
{
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Colors.GridMajor);
    private static readonly IBrush Fill = new ImmutableSolidColorBrush(Colors.Feedback);
    private static readonly IBrush Full = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Label);

    /// <summary>Past this much of the limit the bar turns amber: the next message may be the last.</summary>
    private const double Nearly = 0.85;

    private readonly Button strip = new()
    {
        Name = "context",
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 7),
        CornerRadius = new CornerRadius(0),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private readonly Grid bar = new() { Height = 5, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };

    private readonly Border used = new() { CornerRadius = new CornerRadius(2.5), Background = Fill };

    private readonly TextBlock reading = new()
    {
        Name = "contextReading",
        FontSize = Text.Small,
        FontFamily = Text.Mono,
        Foreground = Ink,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ContentControl chevron = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly UniformGrid breakdown = new()
    {
        Name = "breakdown",
        Columns = 4,
        Margin = new Thickness(12, 0, 12, 10),
        IsVisible = false,
    };

    public ContextStrip()
    {
        Name = "spent";
        IsVisible = false;

        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = Track });
        bar.Children.Add(used);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10 };

        row.Children.Add(Text.Quiet("Context"));
        row.Children.Add(bar);
        row.Children.Add(reading);
        row.Children.Add(chevron);
        Grid.SetColumn(bar, 1);
        Grid.SetColumn(reading, 2);
        Grid.SetColumn(chevron, 3);

        strip.Content = row;
        strip.Click += (_, _) =>
        {
            breakdown.IsVisible = !breakdown.IsVisible;
            ShowChevron();
        };

        Children.Add(strip);
        Children.Add(breakdown);
        ShowChevron();
    }

    protected override Type StyleKeyOverride => typeof(StackPanel);

    /// <summary>Shows <paramref name="tokens"/> against <paramref name="limit"/>, or hides for a conversation that has cost nothing.</summary>
    public void Show(TokensSpent tokens, int turns, int limit)
    {
        IsVisible = !tokens.None;

        if (tokens.None) return;

        var share = limit <= 0 ? 0d : Math.Clamp((double)tokens.Context / limit, 0d, 1d);

        bar.ColumnDefinitions = new ColumnDefinitions($"{share.ToString(CultureInfo.InvariantCulture)}*,{(1 - share).ToString(CultureInfo.InvariantCulture)}*");
        used.Background = share >= Nearly ? Full : Fill;
        reading.Text = $"{TokensSpent.Short(tokens.Context)} / {TokensSpent.Short(limit)}";

        ToolTip.SetTip(strip, "This conversation: " + tokens.Told(turns, limit));

        breakdown.Children.Clear();
        breakdown.Children.Add(Cell("In", tokens.Input));
        breakdown.Children.Add(Cell("Cached", tokens.CacheRead));
        breakdown.Children.Add(Cell("Out", tokens.Output));
        breakdown.Children.Add(Cell(turns == 1 ? "Turn" : "Turns", turns, plain: true));
    }

    private void ShowChevron()
    {
        var mark = breakdown.IsVisible ? Glyphs.Down(12, Text.Muted) : Glyphs.Right(12, Text.Muted);
        mark.Name = breakdown.IsVisible ? "open" : "folded";
        chevron.Content = mark;
    }

    private static StackPanel Cell(string caption, int count, bool plain = false)
    {
        var cell = new StackPanel { Spacing = 1 };

        cell.Children.Add(Text.Quiet(caption, Text.Caption));
        cell.Children.Add(new TextBlock
        {
            Name = "count",
            Text = plain ? count.ToString(CultureInfo.InvariantCulture) : TokensSpent.Short(count),
            FontSize = Text.Emphasis,
            FontFamily = Text.Mono,
            Foreground = Ink,
        });

        return cell;
    }
}
