using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Editor.Statistics;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Composition;

/// <summary>
/// The first-start question: whether to share anonymous usage counts. Asked once,
/// only where this run could send them, which leaves out Android, the web editor and
/// any build without a key (ADR-0094).
/// </summary>
/// <remarks>
/// The two answers weigh the same and neither is the default. Closing the dialog is
/// a no, and is kept as one.
/// </remarks>
internal sealed class UsageQuestion(Usage usage, UsageSection section, IDialog dialog)
{
    public const string Title = "Share anonymous usage counts?";

    public async Task Ask()
    {
        if (!usage.Asks) return;

        section.Answer(await dialog.Show<bool>(Title, View));
    }

    internal static Control View(Action<bool> answer)
    {
        var page = new StackPanel { Width = 480, Spacing = 10 };

        page.Children.Add(new TextBlock
        {
            Text = "Flyback can send a count of how it is used, so that what is worth working "
                + "on is known. It sends nothing unless you say yes, and you can change your "
                + "mind any time under Settings → Privacy.",
            FontSize = Text.Body,
            TextWrapping = TextWrapping.Wrap,
        });

        page.Children.Add(UsageSection.Muted(UsageSection.WhatIsCounted));
        page.Children.Add(UsageSection.Muted(UsageSection.Anonymous));

        var share = new Button { Name = "shareUsage", Content = "Share counts", MinWidth = 96 };
        var keep = new Button { Name = "keepUsage", Content = "Don't share", MinWidth = 96 };

        share.Click += (_, _) => answer(true);
        keep.Click += (_, _) => answer(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { share, keep },
        };

        return new DialogBody(page, buttons) { Name = "usageQuestion" };
    }
}
