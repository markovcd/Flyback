using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// A short choice as a row of buttons a finger can hit, its label above, the chosen one lit in
/// the module's accent: what the card over the live picture shows where the inspector has a list.
/// </summary>
internal sealed class ChoiceChips
{
    /// <summary>The most options drawn as buttons; a longer choice keeps its list.</summary>
    internal const int Most = 12;

    /// <summary>Marks the buttons, so a test can pick them out of the tree.</summary>
    internal const string ChipTag = "choice-chip";

    private readonly List<(ChoiceOption Option, Button Chip)> chips = [];
    private readonly IBrush accent;

    public ChoiceChips(string label, IReadOnlyList<ChoiceOption> options, string chosen, Color accent, Action<string> picked)
    {
        this.accent = new SolidColorBrush(accent);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach (var option in options)
        {
            var chip = new Button
            {
                Content = option.Name,
                Height = 44,
                MinWidth = 44,
                Padding = new Thickness(14, 0),
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = Text.Emphasis,
                CornerRadius = new CornerRadius(22),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            chip.Classes.Add(ChipTag);

            chip.Click += (_, _) =>
            {
                picked(option.Id);
                Paint(option.Id);
            };

            chips.Add((option, chip));
            wrap.Children.Add(chip);
        }

        View = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = label, FontSize = Text.Body, Foreground = new SolidColorBrush(Colors.Muted) },
                wrap,
            },
        };

        Paint(chosen);
    }

    public Control View { get; }

    private void Paint(string chosen)
    {
        foreach (var (option, chip) in chips)
        {
            var lit = option.Id == chosen;

            chip.Background = lit ? accent : new SolidColorBrush(Colors.Node);
            chip.BorderBrush = lit ? accent : new SolidColorBrush(Colors.Outline);
            chip.Foreground = new SolidColorBrush(lit ? Colors.Window : Colors.Value);
            chip.FontWeight = lit ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }
}
