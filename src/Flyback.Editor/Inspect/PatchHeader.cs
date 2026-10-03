using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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
    /// The tags as chips, each with a cross that takes it off, and a dashed one that
    /// turns into a box for one more.
    /// </summary>
    private WrapPanel Tags()
    {
        var chips = new WrapPanel { Name = "patch-tags", Margin = new Thickness(0, 4, 0, 8) };
        var tags = Patch.Tags ?? [];
        var readOnly = document.IsAdrift;

        chips.IsVisible = !readOnly || tags.Count > 0;

        foreach (var tag in tags)
            chips.Children.Add(readOnly ? Chip(tag) : Chip(tag, () => Remove(chips, tag)));

        if (!readOnly && tags.Count < Patch.TagCount) chips.Children.Add(AddChip(chips));

        return chips;
    }

    private Control AddChip(WrapPanel chips)
    {
        var add = new Button
        {
            Name = "patch-tag-add",
            Content = Glyphs.Line(Glyphs.Add(), new TextBlock { Text = "Tag", FontSize = Text.Body }),
            Padding = new Thickness(10, 5, 12, 5),
            MinHeight = 0,
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Text.Muted,
        };

        var slot = Dashed(add, radius: 14, margin: new Thickness(0, 0, 6, 6));
        add.Click += (_, _) => Adding(chips, slot);

        return slot;
    }

    /// <summary>Swaps the dashed chip for a box; Enter or leaving it keeps what was typed, Esc drops it.</summary>
    private void Adding(WrapPanel chips, Control slot)
    {
        var at = chips.Children.IndexOf(slot);
        if (at < 0) return;

        var box = new TextBox
        {
            Name = "patch-tag-new",
            PlaceholderText = "New tag",
            MaxLength = Patch.TagLimit,
            Width = 120,
            MinHeight = 0,
            Height = 30,
            Padding = new Thickness(12, 0),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            FontSize = Text.Body,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var closed = false;

        void Close(bool keep)
        {
            if (closed) return;
            closed = true;

            if (keep) Add(box.Text);

            if (chips.Parent is Panel host && host.Children.IndexOf(chips) is var where and >= 0)
                host.Children[where] = Tags();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;

            e.Handled = true;
            Close(keep: e.Key == Key.Enter);
        };

        box.LostFocus += (_, _) => Close(keep: true);

        chips.Children[at] = box;
        box.Focus();
    }

    /// <summary>Adds what was typed, one tag to a comma, to the tags the patch has.</summary>
    private void Add(string? typed)
    {
        var words = (typed ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        Retag([.. Patch.Tags ?? [], .. words]);
    }

    private void Remove(WrapPanel chips, string tag)
    {
        Retag((Patch.Tags ?? []).Where(t => t != tag));

        if (chips.Parent is Panel host && host.Children.IndexOf(chips) is var where and >= 0)
            host.Children[where] = Tags();
    }

    private void Retag(IEnumerable<string> to)
    {
        var before = Patch.Tags is { } held ? string.Join(' ', held) : null;

        Patch.Tag(to);

        if ((Patch.Tags is { } now ? string.Join(' ', now) : null) != before) Finished();
    }

    private static Border Chip(string text, Action? remove = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = Text.Body,
            Foreground = new SolidColorBrush(Colors.Label),
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (remove is not null)
        {
            var cross = new Button
            {
                Name = "remove-tag-" + text,
                Content = Glyphs.Cross(12, Text.Muted),
                Padding = new Thickness(3),
                Margin = new Thickness(0, -3, -8, -3),
                MinHeight = 0,
                MinWidth = 0,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
            };

            ToolTip.SetTip(cross, "Remove tag " + text);
            cross.Click += (_, _) => remove();
            content.Children.Add(cross);
        }

        return new Border
        {
            Child = content,
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
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
