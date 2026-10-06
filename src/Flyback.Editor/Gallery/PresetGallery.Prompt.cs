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
    /// A card to type an idea in, have the assistant write it out in full where it
    /// stands, and start a new patch from. Closing the gallery stops an expansion
    /// still under way.
    /// </summary>
    private static Border PromptCard(PromptStart start, Action<IPreset?> open, CancellationToken closing)
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

        var note = new TextBlock { Name = "prompt-note", FontSize = Text.Caption, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var expand = new Button { Name = "expand-prompt", Content = "Expand", FontSize = Text.Body, IsEnabled = false };
        var begin = new Button { Name = "start-prompt", Content = "Start", FontSize = Text.Body, IsEnabled = false };

        ToolTip.SetTip(expand, "Have the assistant write this out as a detailed brief, here, for you to edit.");
        ToolTip.SetTip(begin, "Start from an empty patch and send this to the assistant, written out first unless it already is  (Ctrl+Enter)");

        var expanding = false;

        // Set by Expand and kept through edits to what it wrote; cleared with the text.
        var written = false;

        words.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(words.Text)) written = false;
            Enable();
        };

        words.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || !begin.IsEnabled) return;

            Begin();
            e.Handled = true;
        };

        begin.Click += (_, _) => Begin();

        expand.Click += async (_, _) =>
        {
            var idea = words.Text ?? string.Empty;

            expanding = true;
            words.IsReadOnly = true;
            Say("Writing it out…", muted: true);
            Enable();

            (string? Brief, string? Failure) answer;

            try
            {
                answer = await start.Expand(idea, closing);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            expanding = false;
            words.IsReadOnly = false;

            if (answer.Brief is { } brief)
            {
                words.Text = brief;
                words.CaretIndex = brief.Length;
                written = true;
                note.IsVisible = false;
            }
            else
            {
                Say(answer.Failure ?? "The assistant sent nothing back.", muted: false);
            }

            Enable();
        };

        var title = new TextBlock
        {
            Text = "Describe it instead",
            FontSize = Text.Emphasis,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };

        Grid.SetColumn(expand, 1);
        Grid.SetColumn(begin, 2);
        top.Children.Add(title);
        top.Children.Add(expand);
        top.Children.Add(begin);

        expand.Background = Brushes.Transparent;
        expand.BorderBrush = new SolidColorBrush(Colors.Separator);
        expand.BorderThickness = new Thickness(1);
        expand.CornerRadius = begin.CornerRadius = new CornerRadius(8);
        expand.Padding = new Thickness(14, 5);
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
                Children = { top, words, note },
            },
        };

        void Enable()
        {
            var typed = !string.IsNullOrWhiteSpace(words.Text);

            expand.IsEnabled = typed && !expanding;
            begin.IsEnabled = typed && !expanding;
        }

        void Say(string text, bool muted)
        {
            note.Text = text;
            note.Foreground = muted ? Text.Muted : new SolidColorBrush(Colors.Attention);
            note.IsVisible = true;
        }

        void Begin() => open(new PromptedStart((words.Text ?? string.Empty).Trim(), written));
    }
}
