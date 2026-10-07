using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

internal sealed partial class PresetGallery
{
    /// <summary>
    /// A card to type an idea in and start a new patch from.
    /// </summary>
    private static Border PromptCard(Action<IPreset?> open)
    {
        var words = new TextBox
        {
            Name = "prompt-text",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxHeight = 320,
            FontSize = Text.Body,
            PlaceholderText = "Describe what you want built: how it feels, how long it runs, the sounds and the picture.",
        };

        var begin = new Button { Name = "start-prompt", Content = "Start", FontSize = Text.Body, IsEnabled = false };

        ToolTip.SetTip(begin, "Start from an empty patch and send this to the assistant, written out first  (Ctrl+Enter)");

        words.TextChanged += (_, _) => begin.IsEnabled = !string.IsNullOrWhiteSpace(words.Text);

        words.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || !begin.IsEnabled) return;

            Begin();
            e.Handled = true;
        };

        begin.Click += (_, _) => Begin();

        var title = new TextBlock
        {
            Text = "Describe it instead",
            FontSize = Text.Emphasis,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };

        Grid.SetColumn(begin, 1);
        top.Children.Add(title);
        top.Children.Add(begin);

        begin.CornerRadius = new CornerRadius(8);
        begin.Padding = new Thickness(18, 5);
        begin.FontWeight = FontWeight.SemiBold;
        begin.Background = new SolidColorBrush(Accent);
        begin.Foreground = new SolidColorBrush(Colors.Edge);

        return new Border
        {
            Name = "prompt-card",
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { top, words },
            },
        };

        void Begin() => open(new PromptedStart((words.Text ?? string.Empty).Trim()));
    }
}
