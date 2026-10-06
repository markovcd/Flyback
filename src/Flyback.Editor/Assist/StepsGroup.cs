using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Assist;

/// <summary>
/// A run of the assistant's working — what it did, looked at and cost — behind
/// one line, so the transcript can be read as the words alone.
/// </summary>
internal sealed class StepsGroup : StackPanel
{
    private readonly Button header = new()
    {
        Name = "steps",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0, 1),
        MinHeight = 0,
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    private readonly StackPanel body = new() { Spacing = 4, Margin = new Thickness(11, 1, 0, 3) };

    private int count;

    /// <summary>Whether the person has opened or closed it, which settles it for good.</summary>
    private bool touched;

    public StepsGroup()
    {
        header.Click += (_, _) =>
        {
            touched = true;
            body.IsVisible = !body.IsVisible;
            Show();
        };

        Children.Add(header);
        Children.Add(body);
        Show();
    }

    protected override Type StyleKeyOverride => typeof(StackPanel);

    /// <summary>Puts one more step at the end. A picture goes in with the step it belongs to, uncounted.</summary>
    public void Add(Control step, bool counted = true)
    {
        body.Children.Add(step);

        if (counted) count++;

        Show();
    }

    /// <summary>The run is over: it folds away, unless the person already chose how it sits.</summary>
    public void Close()
    {
        if (touched) return;

        body.IsVisible = false;
        Show();
    }

    private void Show()
    {
        var arrow = body.IsVisible ? Glyphs.Down(10) : Glyphs.Right(10);
        arrow.Name = body.IsVisible ? "open" : "folded";

        header.Content = Glyphs.Line(arrow, new TextBlock { Name = "tally", Text = TranscriptView.Tally(count, "step") });
    }
}
