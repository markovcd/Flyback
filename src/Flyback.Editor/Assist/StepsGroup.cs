using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Assist;

/// <summary>
/// A run of the assistant's working — what it did, looked at and cost — behind
/// one line, so the transcript can be read as the words alone.
/// </summary>
internal sealed class StepsGroup : StackPanel
{
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Label);

    /// <summary>How much of the latest step the folded line shows.</summary>
    private const int Widest = 36;

    private readonly Button header = new()
    {
        Name = "steps",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0, 3),
        MinHeight = 0,
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    private readonly StackPanel body = new() { Spacing = 6 };

    private readonly Border card = new()
    {
        Background = new ImmutableSolidColorBrush(Colors.Canvas),
        BorderBrush = new ImmutableSolidColorBrush(Colors.GridMajor),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 2, 0, 4),
    };

    private int count;

    /// <summary>The first line of the newest step, which the header shows beside the count.</summary>
    private string latest = string.Empty;

    /// <summary>Whether the person has opened or closed it, which settles it for good.</summary>
    private bool touched;

    public StepsGroup()
    {
        card.Child = body;

        header.Click += (_, _) =>
        {
            touched = true;
            card.IsVisible = !card.IsVisible;
            Show();
        };

        Children.Add(header);
        Children.Add(card);
        Show();
    }

    protected override Type StyleKeyOverride => typeof(StackPanel);

    /// <summary>Puts one more step at the end. A picture goes in with the step it belongs to, uncounted.</summary>
    public void Add(Control step, bool counted = true, string? text = null)
    {
        body.Children.Add(step);

        if (counted) count++;

        if (text?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) is { } first)
            latest = first.Length > Widest ? string.Concat(first.AsSpan(0, Widest - 1).TrimEnd(), "…") : first;

        Show();
    }

    /// <summary>The run is over: it folds away, unless the person already chose how it sits.</summary>
    public void Close()
    {
        if (touched) return;

        card.IsVisible = false;
        Show();
    }

    private void Show()
    {
        var arrow = card.IsVisible ? Glyphs.Down(10) : Glyphs.Right(10);
        arrow.Name = card.IsVisible ? "open" : "folded";

        var tally = new TextBlock
        {
            Name = "tally",
            Text = TranscriptView.Tally(count, "step"),
            FontWeight = FontWeight.SemiBold,
            Foreground = Ink,
        };

        var line = Glyphs.Line(arrow, tally);

        if (latest.Length > 0) line.Children.Add(new TextBlock { Name = "latest", Text = latest, TextTrimming = TextTrimming.CharacterEllipsis });

        header.Content = line;
    }
}
