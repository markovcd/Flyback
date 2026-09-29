using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Flyback.App.Controls;

namespace Flyback.App.Updates;

/// <summary>
/// The contents of the dialog that says what the release just installed changed.
/// </summary>
/// <remarks>
/// The changelog's Markdown is drawn rather than shown as it was typed, but only as
/// much of it as the changelog uses: a <c>###</c> heading, a <c>-</c> bullet, a plain
/// paragraph, and <c>`code`</c> inside any of them.
/// </remarks>
internal static class WhatsNew
{
    private static readonly FontFamily Code = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>
    /// What the dialog is headed: the release installed, or where more than one
    /// release is shown, the one it was updated from.
    /// </summary>
    public static string Title(ReleaseNotes notes) =>
        notes.Sections.Count > 1 && notes.Since is { } since
            ? $"What's new since Flyback {since.ToString(3)}"
            : $"What's new in Flyback {notes.Version.ToString(3)}";

    public static Control View(ReleaseNotes notes)
    {
        var page = new StackPanel { Name = "whatsNew", Spacing = 6, Width = 520, Margin = new Thickness(20, 12, 20, 20) };

        foreach (var section in notes.Sections)
        {
            // One release's notes need no heading of their own: the title names it.
            if (notes.Sections.Count > 1)
            {
                var release = Inline(section.Heading);

                release.FontSize = Text.Heading;
                release.FontWeight = FontWeight.SemiBold;
                if (page.Children.Count > 0) release.Margin = new Thickness(0, 22, 0, 0);

                page.Children.Add(release);
            }

            Add(page, section.Text);
        }

        return page;
    }

    private static void Add(StackPanel page, string text)
    {
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                var heading = Inline(line[4..]);

                heading.FontSize = Text.Emphasis;
                heading.FontWeight = FontWeight.SemiBold;

                // Apart from the section above it, but not from the first line
                // under it, which is what it heads.
                if (page.Children.Count > 0) heading.Margin = new Thickness(0, 10, 0, 0);

                page.Children.Add(heading);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                page.Children.Add(Bullet(line[2..]));
            }
            else if (line.Trim().Length > 0)
            {
                var paragraph = Inline(line.Trim());

                paragraph.Foreground = Text.Muted;
                page.Children.Add(paragraph);
            }
        }
    }

    private static Control Bullet(string text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*") };

        var mark = new TextBlock { Text = "•", FontSize = Text.Body, Foreground = Text.Muted };
        var body = Inline(text);

        Grid.SetColumn(body, 1);
        row.Children.Add(mark);
        row.Children.Add(body);

        return row;
    }

    /// <summary>A wrapped line, with what the changelog put in backticks set in monospace.</summary>
    private static TextBlock Inline(string text)
    {
        var block = new TextBlock { FontSize = Text.Body, TextWrapping = TextWrapping.Wrap, Inlines = [] };

        var parts = text.Split('`');

        // An odd number of pieces means every backtick was paired; a stray one
        // is shown as typed rather than turning the rest of the line into code.
        if (parts.Length % 2 == 0) parts = [text];

        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0) continue;

            var run = new Run(parts[index]);

            if (index % 2 == 1) run.FontFamily = Code;

            block.Inlines!.Add(run);
        }

        return block;
    }
}
