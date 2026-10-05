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
        ToolTip.SetTip(begin, "Start from an empty patch and send this to the assistant  (Ctrl+Enter)");

        var expanding = false;

        words.TextChanged += (_, _) => Enable();

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
                note.IsVisible = false;
            }
            else
            {
                Say(answer.Failure ?? "The assistant sent nothing back.", muted: false);
            }

            Enable();
        };

        return new Border
        {
            Name = "prompt-card",
            CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Start with a prompt", FontSize = Text.Body, FontWeight = FontWeight.SemiBold },
                    words,
                    note,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { expand, begin } },
                },
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

        void Begin() => open(new PromptedStart((words.Text ?? string.Empty).Trim()));
    }
}
