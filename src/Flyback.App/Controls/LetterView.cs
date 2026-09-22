using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>Writing to Flyback's author, and sending it to the preset site.</summary>
/// <remarks>
/// What the program adds to the letter is printed in it rather than described, so
/// that agreeing to send is agreeing to something that has been read.
/// </remarks>
internal static class LetterView
{
    /// <summary>
    /// Asks over <paramref name="from"/>'s window what there is to say and hands it
    /// to <paramref name="send"/>.
    /// </summary>
    /// <returns>What became of it, or null where nothing was sent.</returns>
    public static async Task<string?> AskAsync(
        Control from, LetterAbout about, Func<string, string, string?, CancellationToken, Task> send)
    {
        if (TopLevel.GetTopLevel(from) is not Window window) return null;

        return await window.ShowDialog<string?>("Write to the author", View(about, send));
    }

    /// <summary>The letter, which answers its dialog with what became of it.</summary>
    internal static Control View(LetterAbout about, Func<string, string, string?, CancellationToken, Task> send)
    {
        var page = new StackPanel { Name = "letter", Spacing = 10, Width = 440, Margin = new Thickness(20, 12, 20, 20) };

        page.Children.Add(new TextBlock
        {
            Text = "Anything you have to say about Flyback — what works, what does not, what is missing — "
                + "goes straight to the person who wrote it.",
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
        });

        var sendButton = new Button { Name = "send", Content = "Send letter", MinWidth = 96, IsEnabled = false };
        var cancel = new Button { Name = "cancel", Content = "Cancel", MinWidth = 96 };
        string? mood = null;

        var message = new TextBox
        {
            Name = "message",
            PlaceholderText = "What you want to say",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = SiteLetters.MessageLimit,
            MinHeight = 108,
            FontSize = Text.Body,
        };

        var moods = new StackPanel { Name = "moods", Spacing = 2 };

        foreach (var (value, said) in SiteLetters.Moods)
        {
            var choice = new RadioButton { Content = said, GroupName = "letter-mood", FontSize = Text.Body, Tag = value };

            choice.IsCheckedChanged += (_, _) =>
            {
                if (choice.IsChecked != true) return;

                mood = value;
                Ready();
            };

            moods.Children.Add(choice);
        }

        page.Children.Add(moods);
        page.Children.Add(message);

        message.TextChanged += (_, _) => Ready();

        var contact = new TextBox
        {
            Name = "contact",
            PlaceholderText = "Where to write back (optional)",
            MaxLength = SiteLetters.ContactLimit,
            FontSize = Text.Body,
        };

        page.Children.Add(contact);

        page.Children.Add(new TextBlock
        {
            Text = "Leave it blank and the letter is anonymous; there is then no way to answer it.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        page.Children.Add(Sent(about));

        var status = new TextBlock
        {
            Name = "letterStatus",
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.Sink),
            IsVisible = false,
        };

        page.Children.Add(status);

        sendButton.Click += async (_, _) =>
        {
            sendButton.IsEnabled = false;
            status.IsVisible = false;

            try
            {
                await send(
                    mood!,
                    message.Text!.Trim(),
                    contact.Text?.Trim() is { Length: > 0 } address ? address : null,
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                status.Text = $"The letter was not sent. {ex.Message}";
                status.IsVisible = true;
                sendButton.IsEnabled = true;
                return;
            }

            Dialog.Close<string?>(sendButton, "Your letter is on its way. Thank you.");
        };

        cancel.Click += (_, _) => Dialog.Close<string?>(cancel, null);

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { sendButton, cancel },
        });

        return page;

        void Ready() => sendButton.IsEnabled = mood is not null && message.Text?.Trim().Length > 0;
    }

    /// <summary>What the program puts in the letter, written out as it will be sent.</summary>
    private static Control Sent(LetterAbout about)
    {
        var block = new StackPanel { Name = "about", Spacing = 3 };

        block.Children.Add(new TextBlock
        {
            Text = "SENT WITH IT",
            FontSize = Text.Caption,
            FontWeight = FontWeight.SemiBold,
            Foreground = Text.Muted,
        });

        foreach (var line in new[] { $"Version {about.Version}", about.Platform, about.Plugins })
            block.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = Text.Small,
                Foreground = Text.Muted,
                TextWrapping = TextWrapping.Wrap,
            });

        block.Children.Add(new TextBlock
        {
            Text = "Nothing else: no patch, no file, no folder and nothing about this machine.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });

        return block;
    }
}
