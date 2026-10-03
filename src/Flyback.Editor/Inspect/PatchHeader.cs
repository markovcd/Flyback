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
    private PatchDescription? description;

    /// <summary>What the heading says of a patch with no file yet.</summary>
    internal const string Unnamed = "Patch";

    private TextBlock? heading;

    /// <summary>The parts, top to bottom, for a panel with nothing selected.</summary>
    internal IEnumerable<Control> Build()
    {
        heading = new TextBlock
        {
            Name = "patch-name",
            Text = Heading(),
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
        if (heading is not null) heading.Text = Heading();
    }

    /// <summary>The name in capitals, as drawn; the file keeps its own case.</summary>
    private string Heading() => (files.Name ?? Unnamed).ToUpperInvariant();

    private Patch Patch => editor.History.Patch;

    private Control Description() => (description ??= new(() => editor.History.Patch, document, Finished)).Shown();

    /// <summary>
    /// Who made the patch: a dashed button to say, a card once it is said, and a box
    /// while it is being typed.
    /// </summary>
    private Control Author()
    {
        if (Patch.Author is not { } author)
            return document.IsAdrift ? new Border { IsVisible = false } : AskingWho();

        return Credited(author);
    }

    private Control AskingWho()
    {
        var ask = new Button
        {
            Name = "patch-author-add",
            Content = Glyphs.Line(
                Glyphs.Person(16, Text.Muted),
                new TextBlock
                {
                    Text = "Click to say who made it.",
                    FontSize = Text.Emphasis,
                    FontStyle = FontStyle.Italic,
                    Foreground = Text.Muted,
                    VerticalAlignment = VerticalAlignment.Center,
                }),
            Padding = new Thickness(12, 0, 14, 0),
            MinHeight = 44,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };

        var slot = Dashed(ask, radius: 10);
        slot.HorizontalAlignment = HorizontalAlignment.Left;
        ask.Click += (_, _) => Asking(slot, held: null);

        return slot;
    }

    private Control Credited(string author)
    {
        var avatar = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Colors.Blend(Colors.Panel, Colors.Source, 0.25)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = author.Trim().Length > 0 ? char.ToUpperInvariant(author.Trim()[0]).ToString() : "?",
                FontSize = Text.Emphasis,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Colors.Source),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        words.Children.Add(Text.Quiet("Made by"));

        words.Children.Add(new TextBlock
        {
            Name = "patch-author",
            Text = author,
            FontSize = Text.Heading,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Colors.Label),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        line.Children.Add(avatar);
        Grid.SetColumn(words, 1);
        line.Children.Add(words);

        var card = new Border
        {
            MinHeight = 56,
            Padding = new Thickness(12, 8, 8, 8),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            Child = line,
        };

        if (document.IsAdrift) return card;

        var edit = Tool("patch-author-edit", "Edit author", Glyphs.Pencil(14, Text.Muted));
        var remove = Tool("patch-author-remove", "Remove author", Glyphs.Cross(14, Text.Muted));
        Grid.SetColumn(edit, 2);
        Grid.SetColumn(remove, 3);
        line.Children.Add(edit);
        line.Children.Add(remove);

        edit.Click += (_, _) => Asking(card, held: author);

        remove.Click += (_, _) =>
        {
            Patch.Credit(null);
            Finished();
            Replace(card, Author());
        };

        return card;
    }

    private static Button Tool(string name, string tip, Control glyph)
    {
        var tool = new Button
        {
            Name = name,
            Content = glyph,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(tool, tip);
        return tool;
    }

    /// <summary>
    /// Swaps <paramref name="shown"/> for a box. Enter or leaving it keeps what was
    /// typed, Esc drops it, and nothing typed leaves who it was.
    /// </summary>
    private void Asking(Control shown, string? held)
    {
        var fill = new SolidColorBrush(Colors.Toolbar);

        var box = new TextBox
        {
            Name = "patch-author-box",
            Text = held ?? "",
            PlaceholderText = "Who made it?",
            MaxLength = Patch.AuthorLimit,
            MinHeight = 0,
            Height = 44,
            Padding = new Thickness(14, 0),
            CornerRadius = new CornerRadius(10),
            FontSize = Text.Heading,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = fill,
        };

        box.Resources["TextControlBackground"] = fill;
        box.Resources["TextControlBackgroundPointerOver"] = fill;
        box.Resources["TextControlBackgroundFocused"] = fill;

        var closed = false;

        void Close(bool keep)
        {
            if (closed) return;
            closed = true;

            var before = Patch.Author;
            if (keep && !string.IsNullOrWhiteSpace(box.Text)) Patch.Credit(box.Text);
            if (Patch.Author != before) Finished();

            Replace(box, Author());
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;

            e.Handled = true;
            Close(keep: e.Key == Key.Enter);
        };

        box.LostFocus += (_, _) => Close(keep: true);

        Replace(shown, box);
        box.Focus();
        box.SelectAll();
    }

    private static void Replace(Control old, Control replacement)
    {
        if (old.Parent is Panel host && host.Children.IndexOf(old) is var at and >= 0) host.Children[at] = replacement;
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

        // The size and place of the chip it replaces, growing only if the tag is longer.
        var chip = new SolidColorBrush(Colors.Toolbar);

        var box = new TextBox
        {
            Name = "patch-tag-new",
            PlaceholderText = "New tag",
            MaxLength = Patch.TagLimit,
            MinWidth = slot.Bounds.Width,
            MinHeight = 0,
            Height = slot.Bounds.Height,
            Padding = new Thickness(12, 0),
            Margin = slot.Margin,
            CornerRadius = new CornerRadius(14),
            FontSize = Text.Body,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = chip,
        };

        box.Resources["TextControlBackground"] = chip;
        box.Resources["TextControlBackgroundPointerOver"] = chip;
        box.Resources["TextControlBackgroundFocused"] = chip;

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

    /// <summary>Finished as a box closes, which the panel is told of only after Enter has taken the box away.</summary>
    private void Finished()
    {
        document.Relaid();
        editor.History.Record();
        document.HandCameOff();
    }
}
