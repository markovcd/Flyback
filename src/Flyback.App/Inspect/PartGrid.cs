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
/// An Arrangement's parts: a map of every level shaded in the module's accent, where a
/// click switches a level off or back on and a drag turns it like a knob, and under it
/// one line of the text language's levels to type into for each part.
/// </summary>
/// <remarks>
/// Typed rather than a knob a cell, because a grid of up to 256 numbers is read as a
/// shape and written as a row, and the row is exactly what a text patch holds.
/// </remarks>
internal sealed class PartGrid
{
    private const double CellHeight = 8;

    /// <summary>How far a drag goes for a part's whole range, as a knob's does; Shift makes it five times finer.</summary>
    private const double Travel = 160;

    /// <summary>How far the pointer moves before a press is a drag rather than a click.</summary>
    private const double Slop = 3;

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

    /// <summary>The cell being dragged, where the drag started and what the level was then.</summary>
    private (Border Cell, int Part, int Section, double Y, float From, float Low, float High, bool Moved)? drag;

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

                Describe(cell, p, s);

                var (at, section) = (p, s);
                cell.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
                cell.PointerPressed += (_, e) => Grab(cell, at, section, e);
                cell.PointerMoved += (_, e) => Turn(e);
                cell.PointerReleased += (_, e) => LetGo(e);
                cell.PointerCaptureLost += (_, _) => drag = null;

                Grid.SetRow(cell, p);
                Grid.SetColumn(cell, s);
                map.Children.Add(cell);
            }
        }

        return map;
    }

    private void Describe(Border cell, int part, int section) =>
        ToolTip.SetTip(cell,
            $"part {part + 1}, section {section + 1}: {ArrangementNotation.Row([parts[part][section]])}. "
            + "Click to switch it off or on, drag up or down to turn it.");

    private void Grab(Border cell, int part, int section, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;

        // The part's own range, so a drag means the same on a row of levels and a row of fold counts.
        var levels = parts[part];
        var high = Math.Max(1f, levels.Max(level => Math.Abs(level.Value)));
        var low = levels.Any(level => level.Value < 0f) ? -high : 0f;

        drag = (cell, part, section, e.GetPosition(cell).Y, levels[section].Value, low, high, false);
        e.Pointer.Capture(cell);
        e.Handled = true;
    }

    private void Turn(PointerEventArgs e)
    {
        if (drag is not { } held) return;

        var rise = held.Y - e.GetPosition(held.Cell).Y;
        if (!held.Moved && Math.Abs(rise) < Slop) return;

        var fine = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 5d : 1d;
        var span = held.High - held.Low;
        var turned = Math.Clamp(held.From + (float)(rise / (Travel * fine)) * span, held.Low, held.High);

        // Hundredths of the range, so a level reads as a number somebody would type.
        var grain = span <= 2f ? 0.01f : 0.1f;
        turned = MathF.Round(turned / grain) * grain;

        drag = held with { Moved = true };
        parts[held.Part][held.Section] = new PartLevel(turned);

        ArrangementExtra.Set(node, parts);
        held.Cell.Background = new SolidColorBrush(Shade(turned / held.High));
        Describe(held.Cell, held.Part, held.Section);

        // One step in the history for the whole drag, as a knob's is.
        changed($"{node.Id} part {held.Part} section {held.Section}");
    }

    private void LetGo(PointerReleasedEventArgs e)
    {
        if (drag is not { } held) return;

        drag = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (held.Moved) Fill();
        else Toggle(held.Part, held.Section);
    }

    /// <summary>
    /// A level that is on goes to nought; one at nought comes back at the part's
    /// strongest level, or at one where the whole part is nought.
    /// </summary>
    internal void Toggle(int part, int section)
    {
        var levels = parts[part];
        var strongest = levels.MaxBy(level => Math.Abs(level.Value)).Value;

        levels[section] = levels[section].Value != 0f
            ? new PartLevel(0f)
            : new PartLevel(strongest != 0f ? strongest : 1f);

        Save();
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

        ToolTip.SetTip(box, "A level for each section: a number, with '>' before one that glides there.");

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
