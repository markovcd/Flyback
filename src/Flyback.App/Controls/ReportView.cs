using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>Asks what is wrong with a shared preset or plugin, and reports it to the preset site's admin.</summary>
internal static class ReportView
{
    /// <summary>
    /// Asks over <paramref name="from"/>'s window what is wrong with <paramref name="name"/>
    /// and hands the answer to <paramref name="send"/>.
    /// </summary>
    /// <returns>What became of it, or null where nothing was sent.</returns>
    public static async Task<string?> AskAsync(Control from, string name, Func<string, string?, CancellationToken, Task> send)
    {
        if (TopLevel.GetTopLevel(from) is not Window window) return null;

        return await window.ShowDialog<string?>($"Report “{name}”", View(name, send));
    }

    /// <summary>The question, which answers its dialog with what became of the report.</summary>
    internal static Control View(string name, Func<string, string?, CancellationToken, Task> send)
    {
        var page = new StackPanel { Name = "report", Spacing = 10, Width = 440, Margin = new Thickness(20, 12, 20, 20) };

        page.Children.Add(new TextBlock
        {
            Text = $"Tell the preset site's admin what is wrong with “{name}”. It stays listed until they have looked.",
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
        });

        var sendButton = new Button { Name = "send", Content = "Send report", MinWidth = 96, IsEnabled = false };
        var cancel = new Button { Name = "cancel", Content = "Cancel", MinWidth = 96 };
        string? reason = null;

        var reasons = new StackPanel { Name = "reasons", Spacing = 2 };

        foreach (var (value, said) in SiteReports.Reasons)
        {
            var choice = new RadioButton { Content = said, GroupName = "report-reason", FontSize = Text.Body, Tag = value };

            choice.IsCheckedChanged += (_, _) =>
            {
                if (choice.IsChecked != true) return;

                reason = value;
                sendButton.IsEnabled = true;
            };

            reasons.Children.Add(choice);
        }

        page.Children.Add(reasons);

        var details = new TextBox
        {
            Name = "details",
            PlaceholderText = "What happened (optional)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = SiteReports.DetailsLimit,
            MinHeight = 72,
            FontSize = Text.Body,
        };

        page.Children.Add(details);

        var status = new TextBlock
        {
            Name = "reportStatus",
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
                await send(reason!, details.Text?.Trim() is { Length: > 0 } said ? said : null, CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                status.Text = $"The report was not sent. {ex.Message}";
                status.IsVisible = true;
                sendButton.IsEnabled = true;
                return;
            }

            Dialog.Close<string?>(sendButton, $"Reported “{name}” to the preset site's admin.");
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
    }
}
