using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// What the inspector says about the patch with nothing selected: its name, what it
/// is for, who made it and its tags, each edited in place.
/// </summary>
/// <remarks>
/// Editable on a locked canvas too: it is written back into the text, as the
/// keyboard's layout is. Not while the text has moved on from the patch, when the
/// line it would land on may not be the one playing.
/// </remarks>
internal sealed class PatchHeader(NodeEditor editor, Document document, PatchFiles files)
{
    /// <summary>What the heading says of a patch with no file yet.</summary>
    internal const string Unnamed = "Patch";

    private TextBlock? heading;

    /// <summary>The parts, top to bottom, for a panel with nothing selected.</summary>
    internal IEnumerable<Control> Build()
    {
        heading = new TextBlock
        {
            Name = "patch-name",
            Text = files.Name ?? Unnamed,
            FontSize = Text.Caption,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 1,
            Foreground = Text.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        return [heading, Description(), Author(), Tags()];
    }

    /// <summary>Redraws the name after the file it was saved to or opened from changed.</summary>
    internal void Rename()
    {
        if (heading is not null) heading.Text = files.Name ?? Unnamed;
    }

    private Patch Patch => editor.History.Patch;

    private TextBlock Description() => Line(
        "patch-description",
        Patch.Description,
        Patch.Description,
        "Double-click to say what this patch is for.",
        "What is this patch for?",
        Patch.DescriptionLimit,
        typed => Patch.Describe(typed),
        () => Patch.Description,
        Description,
        new Thickness(0, 0, 0, 6),
        Text.Heading);

    /// <summary>Who made the patch, as a dashed button with a person on it.</summary>
    private Control Author()
    {
        var text = AuthorText();

        var glyph = Glyphs.Person(14, Text.Muted);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(glyph, Dock.Left);

        var framed = Dashed(
            new DockPanel { Margin = new Thickness(12, 0), Children = { glyph, text } },
            radius: 10);

        framed.MinWidth = 230;
        framed.MinHeight = 38;
        framed.HorizontalAlignment = HorizontalAlignment.Left;
        framed.IsVisible = !document.IsAdrift || Patch.Author is not null;

        return framed;
    }

    private TextBlock AuthorText()
    {
        var line = Line(
            "patch-author",
            Patch.Author is { } author ? "by " + author : null,
            Patch.Author,
            "Double-click to say who made it.",
            "Who made this patch?",
            Patch.AuthorLimit,
            typed => Patch.Credit(typed),
            () => Patch.Author,
            AuthorText,
            new Thickness(0));

        line.VerticalAlignment = VerticalAlignment.Center;
        return line;
    }

    /// <summary>
    /// The tags as chips, with a dashed one to add more. Any of them opens the whole
    /// set as one line of words apart by spaces or commas.
    /// </summary>
    private WrapPanel Tags()
    {
        var chips = new WrapPanel { Name = "patch-tags", Margin = new Thickness(0, 4, 0, 8) };
        var tags = Patch.Tags ?? [];
        var readOnly = document.IsAdrift;

        chips.IsVisible = !readOnly || tags.Count > 0;

        foreach (var tag in tags)
        {
            var chip = Chip(tag);
            if (!readOnly) chip.Click += (_, _) => EditTags(chips);
            chips.Children.Add(chip);
        }

        if (readOnly) return chips;

        var add = Chip("Tag", Glyphs.Add());
        add.Name = "patch-tag-add";
        add.Background = Brushes.Transparent;
        add.BorderThickness = new Thickness(0);
        add.Margin = new Thickness(0);
        add.Click += (_, _) => EditTags(chips);
        chips.Children.Add(Dashed(add, radius: 14, margin: new Thickness(0, 0, 6, 6)));

        return chips;
    }

    private void EditTags(Control chips) =>
        NameBox.Open(
            chips,
            new SolidColorBrush(Colors.Label),
            Patch.Tags is { } held ? string.Join(' ', held) : null,
            "drone slow ambient",
            Patch.TagCount * (Patch.TagLimit + 2),
            typed => Patch.Tag(typed?.Replace(',', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            () => Patch.Tags is { } now ? string.Join(' ', now) : null,
            Tags,
            Finished,
            prose: true);

    private static Button Chip(string text, Control? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        if (glyph is not null) content.Children.Add(glyph);

        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Button
        {
            Content = content,
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Colors.Label),
        };
    }

    /// <summary><paramref name="content"/> in a dashed outline, which says it is there to be filled in.</summary>
    private static Grid Dashed(Control content, double radius, Thickness margin = default)
    {
        var outline = new Rectangle
        {
            RadiusX = radius,
            RadiusY = radius,
            Stroke = new SolidColorBrush(Colors.Separator),
            StrokeThickness = 1,
            StrokeDashArray = [3, 3],
            IsHitTestVisible = false,
        };

        return new Grid { Margin = margin, Children = { outline, content } };
    }

    /// <summary>
    /// One thing said about the whole patch, which a double-click turns into a box
    /// to write it in.
    /// </summary>
    /// <param name="shown">What the panel shows, and null where nothing is said.</param>
    /// <param name="held">What the box opens holding.</param>
    /// <param name="asking">What the panel shows in its place where nothing is said.</param>
    private TextBlock Line(
        string name,
        string? shown,
        string? held,
        string asking,
        string fallback,
        int limit,
        Action<string?> set,
        Func<string?> current,
        Func<Control> rebuild,
        Thickness margin,
        double size = Text.Body)
    {
        var ink = new SolidColorBrush(Colors.Label);

        var line = new TextBlock
        {
            Name = name,
            Text = shown ?? asking,
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontStyle = shown is null ? FontStyle.Italic : FontStyle.Normal,
            Foreground = shown is null ? Text.Muted : ink,
            Background = Brushes.Transparent,
            Margin = margin,
        };

        if (document.IsAdrift)
        {
            line.IsVisible = shown is not null;
            return line;
        }

        if (shown is not null) ToolTip.SetTip(line, "Double-click to change it. Empty the box to take it away.");

        line.Cursor = NameBox.Renaming;

        line.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            NameBox.Open(line, ink, held, fallback, limit, set, current, rebuild, Finished, prose: true);
        };

        return line;
    }

    /// <summary>
    /// Finished as the box closes: Enter takes the box away before any key comes up
    /// in the panel to say so.
    /// </summary>
    private void Finished()
    {
        document.Relaid();
        editor.History.Record();
        document.HandCameOff();
    }
}
