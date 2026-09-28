using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Core.Language;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Inspect;

/// <summary>
/// An Arrangement's parts: a map of every level shaded in the module's accent, and
/// under it one line of the text language's levels to type into for each part.
/// </summary>
/// <remarks>
/// Typed rather than a knob a cell, because a grid of up to 256 numbers is read as a
/// shape and written as a row, and the row is exactly what a text patch holds.
/// </remarks>
internal sealed class PartGrid
{
    private const double CellHeight = 8;

    /// <summary>The columns of a part's row: its name, its levels, its remove button. The map lines up with the middle one.</summary>
    private const double NameWidth = 44, RemoveWidth = 22;

    /// <summary>Marks the boxes a part's levels are typed into, for the UI tests.</summary>
    internal const string RowTag = "part-row";

    /// <summary>Marks the shaded cells of the map, for the UI tests.</summary>
    internal const string CellTag = "part-cell";

    private readonly NodeInstance node;
    private readonly Color accent;
    private readonly Action<string?> changed;

    /// <summary>The parts being edited, written back by <see cref="Save"/> at every change.</summary>
    private readonly List<List<PartLevel>> parts;

    private readonly StackPanel body = new();

    public PartGrid(NodeInstance node, NodeDef def, Action<string?> changed)
    {
        this.node = node;
        this.changed = changed;
        accent = Colors.Palette(def).Accent;
        parts = ArrangementExtra.Tidy(ArrangementExtra.Of(node));

        View = new StackPanel
        {
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "arrangement",
                    FontSize = Text.Micro,
                    Foreground = Text.Muted,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                body,
            },
        };

        Fill();
    }

    public Control View { get; }

    private int Sections => parts.Count == 0 ? 0 : parts[0].Count;

    private void Fill()
    {
        body.Children.Clear();

        if (parts.Count == 0)
            body.Children.Add(Text.Quiet("No parts yet: every part holds at nought until it has one."));
        else
            body.Children.Add(Map());

        for (var i = 0; i < parts.Count; i++) body.Children.Add(Row(i));

        body.Children.Add(Buttons());
    }

    /// <summary>Puts the parts back on the node and says so, which files the change in the history.</summary>
    private void Save()
    {
        var tidy = ArrangementExtra.Tidy(parts);

        parts.Clear();
        parts.AddRange(tidy);

        ArrangementExtra.Set(node, parts);
        Fill();
        changed(null);
    }

    /// <summary>
    /// Every level as a cell, brighter the higher it is within its own part, and a
    /// gliding one shaded from where it comes from to where it goes.
    /// </summary>
    private Control Map()
    {
        var map = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", Sections))),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", parts.Count))),
            Margin = new Thickness(NameWidth, 0, RemoveWidth, 6),
        };

        for (var p = 0; p < parts.Count; p++)
        {
            var part = parts[p];
            var highest = Math.Max(part.Max(level => Math.Abs(level.Value)), 1e-6f);

            for (var s = 0; s < part.Count; s++)
            {
                var was = part[(s + part.Count - 1) % part.Count].Value / highest;
                var now = part[s].Value / highest;

                var cell = new Border
                {
                    Tag = CellTag,
                    Height = CellHeight,
                    Margin = new Thickness(0.5, 1),
                    Background = part[s].Glides ? Gradient(was, now) : new SolidColorBrush(Shade(now)),
                };

                ToolTip.SetTip(cell, $"part {p + 1}, section {s + 1}: {ArrangementNotation.Row([part[s]])}");

                Grid.SetRow(cell, p);
                Grid.SetColumn(cell, s);
                map.Children.Add(cell);
            }
        }

        return map;
    }

    private Color Shade(float share) => Colors.Blend(Colors.Node, accent, Math.Clamp(Math.Abs(share), 0f, 1f));

    private LinearGradientBrush Gradient(float from, float to) => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Shade(from), 0), new GradientStop(Shade(to), 1) },
    };

    /// <summary>A part's name, its levels to type into, and the button that takes it away.</summary>
    private Control Row(int index)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{NameWidth},*,{RemoveWidth}"),
            Margin = new Thickness(0, 1),
        };

        var name = new TextBlock
        {
            Text = $"part {index + 1}",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var written = ArrangementNotation.Row(parts[index]);

        var box = new TextBox
        {
            Tag = RowTag,
            Text = written,
            FontSize = Text.Small,
            MinHeight = 23,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(box, "A level for each section: a number, '~' for nought, '>' before one that glides there.");

        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    Keep();
                    break;

                case Key.Escape:
                    box.Text = written;
                    break;

                default:
                    return;
            }

            e.Handled = true;
        };

        box.LostFocus += (_, _) => Keep();

        var remove = Small("✕", "Remove this part");
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.Click += (_, _) =>
        {
            parts.RemoveAt(index);
            Save();
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(remove, 2);
        row.Children.Add(name);
        row.Children.Add(box);
        row.Children.Add(remove);

        return row;

        void Keep()
        {
            var typed = box.Text ?? string.Empty;
            if (typed == written) return;

            var issues = new List<LanguageIssue>();
            var read = typed.Contains('|') ? null : ArrangementNotation.Read(typed, 1, 1, issues);

            if (read is not { Count: 1 } || issues.Count > 0)
            {
                box.BorderBrush = new SolidColorBrush(Colors.Attention);
                ToolTip.SetTip(box, issues.Count > 0 ? issues[0].Message : "One part's levels, with no '|': add a part with the button below.");
                return;
            }

            parts[index] = read[0];
            Save();
        }
    }

    private Control Buttons()
    {
        var add = Wide("+ part", "Add a part, at nought in every section", parts.Count < NodeCatalog.MaxParts);
        add.Click += (_, _) =>
        {
            parts.Add([.. Enumerable.Repeat(new PartLevel(0f), Math.Max(Sections, 1))]);
            Save();
        };

        var longer = Wide("+ section", "Add a section at the end, each part holding its last level", parts.Count > 0 && Sections < NodeCatalog.MaxSections);
        longer.Click += (_, _) =>
        {
            foreach (var part in parts) part.Add(part[^1] with { Glides = false });
            Save();
        };

        var shorter = Wide("− section", "Take the last section away", Sections > 1);
        shorter.Click += (_, _) =>
        {
            foreach (var part in parts) part.RemoveAt(part.Count - 1);
            Save();
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { add, longer, shorter },
        };
    }

    private static Button Wide(string text, string tip, bool enabled)
    {
        var button = new Button
        {
            Content = text,
            FontSize = Text.Small,
            Padding = new Thickness(8, 2),
            IsEnabled = enabled,
        };

        ToolTip.SetTip(button, tip);

        return button;
    }

    private static Button Small(string text, string tip)
    {
        var button = new Button
        {
            Content = text,
            FontSize = Text.Caption,
            Padding = new Thickness(0),
            Width = 18,
            Height = 18,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Opacity = 0.45,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(button, tip);
        button.PointerEntered += (_, _) => button.Opacity = 1;
        button.PointerExited += (_, _) => button.Opacity = 0.45;

        return button;
    }
}
