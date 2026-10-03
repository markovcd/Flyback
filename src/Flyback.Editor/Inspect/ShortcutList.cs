using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The groups of <see cref="HelpGroup"/> as headings that fold open onto their rows,
/// each row a title, a line saying more, and the keys that do it.
/// </summary>
internal static class ShortcutList
{
    /// <summary>The heading's name is <c>shortcuts:</c> and the group's title, for a test to find it by.</summary>
    internal const string HeadingPrefix = "shortcuts:";

    /// <summary>Named so a test can find the keys drawn on a row.</summary>
    internal const string KeyName = "keycap";

    /// <summary>
    /// Builds the list. <paramref name="open"/> holds the titles of the groups that
    /// are unfolded, and a group opened or shut is added to it or taken out, so the
    /// next list built from the same set comes up the same.
    /// </summary>
    internal static Control Of(IReadOnlyList<HelpGroup> groups, ISet<string> open)
    {
        var list = new StackPanel { Spacing = 1 };

        foreach (var group in groups) list.Children.Add(Folding(group, open));

        return new Border
        {
            Name = "shortcuts",
            Background = new SolidColorBrush(Colors.Separator),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = list,
        };
    }

    private static Control Folding(HelpGroup group, ISet<string> open)
    {
        var rows = new StackPanel
        {
            Background = new SolidColorBrush(Colors.Canvas),
            Spacing = 0,
            IsVisible = open.Contains(group.Title),
        };

        foreach (var row in group.Rows) rows.Children.Add(Row(row));

        var arrow = new ContentControl();
        var heading = new Button
        {
            Name = HeadingPrefix + group.Title,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Panel),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(10, 9),
            Content = Header(group, arrow),
        };

        void Show() => arrow.Content = rows.IsVisible ? Glyphs.Down(10) : Glyphs.Right(10);

        heading.Click += (_, _) =>
        {
            rows.IsVisible = !rows.IsVisible;
            if (rows.IsVisible) open.Add(group.Title); else open.Remove(group.Title);
            Show();
        };

        Show();

        return new StackPanel { Children = { heading, rows } };
    }

    private static Control Header(HelpGroup group, Control arrow)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        line.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontSize = Text.Emphasis,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Colors.Label),
        });

        var count = Text.Quiet(group.Rows.Count.ToString());
        count.Margin = new Thickness(0, 0, 8, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 1);
        line.Children.Add(count);

        arrow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(arrow, 2);
        line.Children.Add(arrow);

        return line;
    }

    private static Control Row(HelpRow row)
    {
        var words = new StackPanel { Spacing = 2 };

        words.Children.Add(new TextBlock
        {
            Text = row.Title,
            FontSize = Text.Body,
            Foreground = new SolidColorBrush(Colors.Label),
            TextWrapping = TextWrapping.Wrap,
        });

        if (row.Detail.Length > 0)
        {
            var detail = Text.Quiet(row.Detail);
            detail.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(detail);
        }

        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(10, 7),
        };

        line.Children.Add(words);

        if (row.Keys.Length > 0)
        {
            var keys = Keys(row.Keys);
            Grid.SetColumn(keys, 1);
            line.Children.Add(keys);
        }

        return line;
    }

    private static Control Keys(string keys)
    {
        var caps = new WrapPanel
        {
            MaxWidth = 130,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 1, 0, 0),
        };

        var alternatives = keys.Split(" / ");

        for (var a = 0; a < alternatives.Length; a++)
        {
            if (a > 0) caps.Children.Add(Text.Quiet("/"));

            foreach (var key in alternatives[a].Split('+')) caps.Children.Add(Cap(key));
        }

        return caps;
    }

    private static Control Cap(string key) => new Border
    {
        Name = KeyName,
        Background = new SolidColorBrush(Colors.NodeSelected),
        BorderBrush = new SolidColorBrush(Colors.Separator),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(5, 1),
        Margin = new Thickness(2, 1),
        Child = new TextBlock
        {
            Text = key,
            FontSize = Text.Small,
            Foreground = new SolidColorBrush(Colors.Label),
        },
    };
}
