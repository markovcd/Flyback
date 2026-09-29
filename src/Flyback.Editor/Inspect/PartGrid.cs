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
/// An Arrangement's parts: a map of every level shaded in the module's accent, a row a
/// part, where a click switches a level off or back on, a double-click makes it glide or
/// hold, and a drag turns it like a knob.
/// </summary>
/// <remarks>
/// A cell rather than a knob for each level, because a grid of up to 256 numbers is read
/// as a shape; the exact numbers are in each cell's tooltip and in the text.
/// </remarks>
internal sealed class PartGrid
{
    private const double CellHeight = 14;

    /// <summary>How far a drag goes for a part's whole range, as a knob's does; Shift makes it five times finer.</summary>
    private const double Travel = 160;

    /// <summary>How far the pointer moves before a press is a drag rather than a click.</summary>
    private const double Slop = 3;

    /// <summary>How far a drag holds at nought on its way through, so nought is easy to land on.</summary>
    private const double Catch = 12;

    /// <summary>Either side of a part's cells: its name, and the button that takes it away.</summary>
    private const double NameWidth = 44, RemoveWidth = 22;

    /// <summary>Marks the buttons that take a part away, for the UI tests.</summary>
    internal const string RemoveTag = "part-remove";

    /// <summary>Marks the shaded cells of the map, for the UI tests.</summary>
    internal const string CellTag = "part-cell";

    private readonly NodeInstance node;
    private readonly Color accent;

    /// <summary>What a level below nought is shaded in: the accent turned half-way round the color wheel.</summary>
    private readonly Color below;
    private readonly Action<string?> changed;

    /// <summary>The parts being edited, written back by <see cref="Save"/> at every change.</summary>
    private readonly List<List<PartLevel>> parts;

    private readonly StackPanel body = new();

    private static readonly Cursor Upright = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor Hidden = new(StandardCursorType.None);

    /// <summary>The cell being dragged, and how far.</summary>
    private Held? drag;

    /// <summary>Holds the pointer still while a level is dragged, as a knob does, or leaves it free.</summary>
    internal IPointerAnchors Anchors { get; set; } = PlatformAnchors.Instance;

    /// <summary>
    /// What a drag's level is filed under until it is let go. The cell follows every move,
    /// and the patch, which recompiles both programs when told, hears it once at the end.
    /// </summary>
    private string? unheard;

    /// <summary>The cell a click last switched and what it was, which a double-click puts back before it glides.</summary>
    private (int Part, int Section, PartLevel Before)? clicked;

    public PartGrid(NodeInstance node, NodeDef def, Action<string?> changed)
    {
        this.node = node;
        this.changed = changed;
        accent = Colors.Palette(def).Accent;

        var hsl = accent.ToHsl();
        below = new HslColor(hsl.A, (hsl.H + 180) % 360, hsl.S, hsl.L).ToRgb();
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

        body.Children.Add(Buttons());
    }

    /// <summary>Puts the parts back on the node and says so, which files the change in the history.</summary>
    /// <param name="because">What the change is filed under, so edits under the same name are one step.</param>
    private void Save(string? because = null)
    {
        var tidy = ArrangementExtra.Tidy(parts);

        parts.Clear();
        parts.AddRange(tidy);

        ArrangementExtra.Set(node, parts);
        Fill();
        changed(because);
    }

    /// <summary>
    /// Every level as a cell, dark at nought and fully lit at one or more, in another
    /// color below nought, and a
    /// gliding one shaded from where it comes from to where it goes; each part named on
    /// its left and taken away on its right.
    /// </summary>
    private Control Map()
    {
        var map = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                string.Join(',', [NameWidth.ToString(), .. Enumerable.Repeat("*", Sections), RemoveWidth.ToString()])),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", parts.Count))),
        };

        for (var p = 0; p < parts.Count; p++)
        {
            var part = parts[p];

            var name = new TextBlock
            {
                Text = $"part {p + 1}",
                FontSize = Text.Small,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var index = p;
            var remove = Small(Glyphs.Cross(12), "Remove this part");
            remove.Tag = RemoveTag;
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.Click += (_, _) =>
            {
                parts.RemoveAt(index);
                Save();
            };

            Grid.SetRow(name, p);
            Grid.SetRow(remove, p);
            Grid.SetColumn(remove, Sections + 1);
            map.Children.Add(name);
            map.Children.Add(remove);

            for (var s = 0; s < part.Count; s++)
            {
                var was = part[(s + part.Count - 1) % part.Count].Value;
                var now = part[s].Value;

                var cell = new Border
                {
                    Tag = CellTag,
                    Height = CellHeight,
                    Margin = new Thickness(0.5, 1),
                    Background = part[s].Glides ? Gradient(was, now) : new SolidColorBrush(Shade(now)),
                };

                Describe(cell, p, s);

                var (at, section) = (p, s);
                cell.Cursor = Upright;
                cell.PointerPressed += (_, e) => Grab(cell, at, section, e);
                cell.PointerMoved += (_, e) => Turn(e);
                cell.PointerReleased += (_, e) => LetGo(e);
                cell.PointerCaptureLost += (_, _) =>
                {
                    if (drag is not { } held) return;

                    drag = null;
                    held.Release();
                    Heard();
                };

                Grid.SetRow(cell, p);
                Grid.SetColumn(cell, s + 1);
                map.Children.Add(cell);
            }
        }

        return map;
    }

    private void Describe(Border cell, int part, int section) =>
        ToolTip.SetTip(cell,
            $"part {part + 1}, section {section + 1}: {ArrangementNotation.Row([parts[part][section]])}. "
            + "Click to switch it off or on, double-click to make it glide, drag up or down to turn it.");

    private void Grab(Border cell, int part, int section, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2 && clicked is { } first && first.Part == part && first.Section == section)
        {
            Glide(part, section);
            e.Handled = true;
            return;
        }

        // The part's own range either side of nought, so a drag means the same on a row of
        // levels and a row of fold counts.
        var levels = parts[part];
        var high = Math.Max(1f, levels.Max(level => Math.Abs(level.Value)));

        drag = new Held(cell, part, section, e.GetPosition(cell), levels[section].Value, -high, high, e.Pointer.Type == PointerType.Touch ? null : Anchors.Take(cell));
        e.Pointer.Capture(cell);
        e.Handled = true;
    }

    private void Turn(PointerEventArgs e)
    {
        if (drag is not { } held) return;

        var at = e.GetPosition(held.Cell);

        // The warp's own echo, which would otherwise count as a move.
        if (held.Anchor is not null && at == held.Home) return;

        held.Rise += held.Last.Y - at.Y;
        held.Last = held.Anchor?.Return() == true ? held.Home : at;

        var rise = held.Rise;
        if (!held.Moved && Math.Abs(rise) < Slop) return;

        var fine = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 5d : 1d;
        var perPixel = held.High / (Travel * fine);

        // Where the drag passes nought, in pixels, and the catch that holds it there.
        var nought = -held.From / perPixel;
        if (held.From >= 0f && rise < nought) rise = Math.Min(nought, rise + Catch);
        else if (held.From < 0f && rise > nought) rise = Math.Max(nought, rise - Catch);

        var turned = Math.Clamp(held.From + (float)(rise * perPixel), held.Low, held.High);

        // Hundredths of the range, so a level reads as a number somebody would type.
        var grain = held.High <= 2f ? 0.01f : 0.1f;
        turned = MathF.Round(turned / grain) * grain;

        held.Moved = true;
        parts[held.Part][held.Section] = parts[held.Part][held.Section] with { Value = turned };

        held.Cell.Background = new SolidColorBrush(Shade(turned));
        Describe(held.Cell, held.Part, held.Section);

        unheard = $"{node.Id} part {held.Part} section {held.Section}";
    }

    /// <summary>Puts the dragged level on the node and tells the patch.</summary>
    private void Heard()
    {
        if (unheard is not { } because) return;

        unheard = null;
        ArrangementExtra.Set(node, parts);
        changed(because);
    }

    private void LetGo(PointerReleasedEventArgs e)
    {
        if (drag is not { } held) return;

        drag = null;
        held.Release();
        e.Pointer.Capture(null);
        e.Handled = true;

        Heard();

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

        clicked = (part, section, levels[section]);

        levels[section] = levels[section].Value != 0f
            ? new PartLevel(0f)
            : new PartLevel(strongest != 0f ? strongest : 1f);

        Save(Cell(part, section));
    }

    /// <summary>
    /// A double-click: the level its first click switched is put back, and made to glide
    /// there across its section, or to hold where it glided. Filed with that click, so the
    /// two are one step.
    /// </summary>
    internal void Glide(int part, int section)
    {
        var before = clicked is { } first && first.Part == part && first.Section == section
            ? first.Before
            : parts[part][section];

        clicked = null;
        parts[part][section] = before with { Glides = !before.Glides };

        Save(Cell(part, section));
    }

    private string Cell(int part, int section) => $"{node.Id} part {part} section {section} click";

    /// <summary>A level's shade: nought is the unlit cell and one or more the full accent; below nought, the other color by the level's size.</summary>
    private Color Shade(float level) =>
        Colors.Blend(Colors.Node, level < 0f ? below : accent, Math.Clamp(Math.Abs(level), 0f, 1f));

    private LinearGradientBrush Gradient(float from, float to) => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Shade(from), 0), new GradientStop(Shade(to), 1) },
    };

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

    private static Button Small(Control icon, string tip)
    {
        var button = new Button
        {
            Content = icon,
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

    /// <summary>
    /// A drag under way: where it began, how far it has risen, and the anchor that holds the
    /// pointer, which hides it for as long as it holds.
    /// </summary>
    private sealed class Held
    {
        public Held(Border cell, int part, int section, Point home, float from, float low, float high, IPointerAnchor? anchor)
        {
            (Cell, Part, Section, Home, Last, From, Low, High, Anchor) = (cell, part, section, home, home, from, low, high, anchor);

            if (anchor is not null) cell.Cursor = PartGrid.Hidden;
        }

        public Border Cell { get; }
        public int Part { get; }
        public int Section { get; }
        public Point Home { get; }
        public float From { get; }
        public float Low { get; }
        public float High { get; }
        public IPointerAnchor? Anchor { get; }

        public Point Last { get; set; }
        public double Rise { get; set; }
        public bool Moved { get; set; }

        /// <summary>Lets the pointer go and shows it again.</summary>
        public void Release()
        {
            Anchor?.Dispose();
            Cell.Cursor = Upright;
        }
    }
}
