using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The tune a sequencer plays, as a list you can add to, take from and reorder.
/// </summary>
/// <remarks>
/// Composed from ordinary controls rather than drawn, which is the opposite call
/// to <see cref="NodeEditor"/>'s. That control draws itself because it zooms and
/// because a wire has to end exactly where a socket was painted
/// ([0017](0017-draw-the-node-editor-in-one-control.md)); neither is true of a
/// list in a panel, and drawing one by hand would mean hand-rolling text entry
/// and giving up the keyboard — the costs that record accepted for the canvas
/// and has no reason to accept here. The one exception is the volume, which is
/// a <see cref="LevelBar"/>: thirty-two sliders with thumbs on them read as
/// thirty-two controls rather than as a pattern.
/// </remarks>
internal sealed class StepList
{
    private const double RowHeight = 28;
    private const double InsertHeight = 6;

    /// <summary>The height of an insert strip, so a test can pick one out of the tree.</summary>
    internal const double InsertHeightForTests = InsertHeight;
    private const double ControlHeight = 23;

    /// <summary>Marks the grids that are rows of this list, for the UI tests.</summary>
    internal const string RowTag = "step-row";

    private static readonly IBrush Faint = new SolidColorBrush(Colors.Muted);
    private static readonly IBrush Accent = new SolidColorBrush(Colors.Source);

    private readonly NodeInstance node;
    private readonly StepSpec spec;
    /// <summary>
    /// Told that the notes changed, and under what name to file it. A volume is
    /// a bar one drags, so a note's own edits carry the row they came from and
    /// fold into one step; adding, removing and reordering are discrete and
    /// carry nothing.
    /// </summary>
    private readonly Action<string?> changed;

    private readonly StackPanel rows = new();

    /// <summary>
    /// The tune being edited. Held here rather than read back off the node on
    /// every touch: a list is inserted into, taken from and reordered in place,
    /// and <see cref="StepsExtra.Of"/> hands back a fresh copy each time it is
    /// asked. Written back by <see cref="Save"/> at every point the patch is
    /// told something changed, so the two never disagree for longer than one
    /// statement.
    /// </summary>
    private readonly List<Step> notes;

    private Control? dragging;
    private int dragFrom;
    private int dragTo;
    private Point dragOrigin;

    public StepList(NodeInstance node, StepSpec spec, Action<string?> changed)
    {
        this.node = node;
        this.spec = spec;
        this.changed = changed;

        notes = StepsExtra.Of(node);

        var panel = new StackPanel
        {
            Margin = new Thickness(0, 14, 0, 0),
            Children = { Heading(), rows },
        };

        // Fluent sizes a text box for a form, and a tune is a list of thirty-two
        // of them. Both of its natural sizes have to be undone, and both live on
        // the TextBox inside the template rather than on the NumericUpDown:
        // setting the height outside squashes the frame and hides the number,
        // and leaving the width alone makes a box wider than its column, which
        // a Grid answers by letting it spill over its neighbours.
        panel.Styles.Add(new Style(x => x.OfType<NumericUpDown>().Descendant().OfType<TextBox>())
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, ControlHeight),
                new Setter(Layoutable.MinWidthProperty, 0d),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(5, 0)),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        });

        panel.Styles.Add(new Style(x => x.OfType<NumericUpDown>())
        {
            Setters = { new Setter(Layoutable.MinWidthProperty, 0d) },
        });

        View = panel;

        Fill();
    }

    public Control View { get; }

    /// <summary>Whether a value stands for something other than itself — a note number.</summary>
    private bool Named => spec.Display == PortDisplay.Note;

    /// <summary>
    /// handle · number · value · [name] · length · volume · remove.
    /// The volume takes the slack, because it is the only one of them that reads
    /// as a shape across the whole list rather than as a number on its own row.
    /// </summary>
    private string Columns => Named ? "16,20,58,34,50,*,22" : "16,20,58,50,*,22";

    private Control Heading()
    {
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(Columns),
            Margin = new Thickness(0, 0, 0, 3),
        };

        head.Children.Add(Column(Label(Named ? "note" : "value"), 2));
        head.Children.Add(Column(Label("length"), Named ? 4 : 3));
        head.Children.Add(Column(Label("volume"), Named ? 5 : 4));

        return head;
    }

    // --- building ----------------------------------------------------------------

    /// <summary>
    /// Rebuilt whole on any change of shape. A sequence is at most thirty-two
    /// rows and the panel is rebuilt on every selection anyway, so there is
    /// nothing here worth the bookkeeping of patching it in place.
    /// </summary>
    private void Fill()
    {
        rows.Children.Clear();

        for (var i = 0; i < notes.Count; i++)
        {
            rows.Children.Add(Inserter(i));
            rows.Children.Add(Row(i));
        }

        rows.Children.Add(Inserter(notes.Count));

        if (notes.Count == 0)
            rows.Children.Add(new TextBlock
            {
                Text = "No notes yet — the sequencer holds still until it has one.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.5,
                Margin = new Thickness(2, 4, 2, 0),
            });
    }

    private void Rebuild()
    {
        Save();
        Fill();
        changed(null);
    }

    /// <summary>
    /// Puts the tune back on the node. Called on every change rather than when
    /// the panel closes, because there is no moment a panel closes: a patch is
    /// snapshotted for the undo stack the instant <see cref="changed"/> is told,
    /// and a tune still sitting in this list at that moment is a tune the
    /// snapshot did not get.
    /// </summary>
    private void Save() => StepsExtra.Set(node, notes);

    /// <summary>
    /// The gap between two notes, and the way one is put there. Nearly invisible
    /// until the pointer finds it, so thirty-three of them do not read as
    /// thirty-three controls.
    /// </summary>
    private Control Inserter(int at)
    {
        var room = notes.Count < NodeCatalog.MaxSteps;

        var line = new Border
        {
            Height = 2,
            Background = Accent,
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 22, 0),
        };

        var strip = new Panel
        {
            Height = InsertHeight,
            Background = Brushes.Transparent,
            Cursor = room ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            Children = { line },
        };

        if (!room)
        {
            ToolTip.SetTip(strip, $"A sequence holds at most {NodeCatalog.MaxSteps} notes.");
            return strip;
        }

        ToolTip.SetTip(strip, "Add a note here");

        strip.PointerEntered += (_, _) => line.Opacity = 0.85;
        strip.PointerExited += (_, _) => line.Opacity = 0;

        strip.PointerPressed += (_, _) =>
        {
            // Copied from the note it follows, so adding to a tune extends it
            // rather than dropping a stranger into the middle of it.
            var like = notes.Count == 0 ? new Step(Middle) : notes[Math.Max(at - 1, 0)];

            notes.Insert(at, like);
            Rebuild();
        };

        return strip;
    }

    /// <summary>A value in the middle of the range, for the very first note of an empty list.</summary>
    private float Middle => (spec.Range.Min + spec.Range.Max) / 2f;

    private Control Row(int index)
    {
        var step = notes[index];

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(Columns),
            Height = RowHeight,

            // Named so a test can find a row without having to guess which of
            // the grids in the tree are rows and which belong to a template.
            Tag = RowTag,
        };

        var handle = new TextBlock
        {
            // Three bars, chosen because the shipped font actually has them.
            // The six braille dots conventional for a grip do not: on a machine
            // with nothing to fall back to, any bare Linux one, that glyph draws
            // nothing at all. This one is visible everywhere the program runs.
            Text = "≡",
            FontSize = 13,
            Foreground = Faint,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.SizeAll),

            // Drawing it is not the same as being able to grab it. Without a
            // brush behind it a control is hit-tested against the ink itself, so
            // the gaps between the bars would be holes a pointer falls through
            // to the panel below. Filling it makes the box the target, which is
            // what a handle should be either way.
            Background = Brushes.Transparent,
        };

        ToolTip.SetTip(handle, "Drag to reorder");
        Reorder(handle, row, index);

        var value = Number(step.Value, spec.Range.Min, spec.Range.Max, Named ? 1m : 0.05m,
            Named ? "0" : "0.##",
            v => Set(index, s => s with { Value = v }));

        var length = Number(step.Length, Step.ShortestLength, 16f, 0.25m, "0.##",
            v => Set(index, s => s with { Length = v }));

        ToolTip.SetTip(length, "How long this note lasts, in steps. 2 is twice as long as 1.");

        var volume = new LevelBar
        {
            Value = step.Volume,
            Margin = new Thickness(3, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(volume, "How loud, and a rest at nothing — a level rather than a switch.");
        volume.ValueChanged += v => Set(index, s => s with { Volume = (float)v });

        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(0),
            Width = 18,
            Height = 18,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Opacity = 0.45,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ToolTip.SetTip(remove, "Remove this note");
        remove.PointerEntered += (_, _) => remove.Opacity = 1;
        remove.PointerExited += (_, _) => remove.Opacity = 0.45;
        remove.Click += (_, _) =>
        {
            notes.RemoveAt(index);
            Rebuild();
        };

        var column = 0;
        row.Children.Add(Column(handle, column++));
        row.Children.Add(Column(new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 10.5,
            Opacity = 0.4,
            VerticalAlignment = VerticalAlignment.Center,
        }, column++));

        row.Children.Add(Column(value, column++));

        if (Named)
            row.Children.Add(Column(new TextBlock
            {
                Text = spec.AsPort.Format(step.Value),
                FontSize = 11.5,
                Opacity = 0.75,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            }, column++));

        row.Children.Add(Column(length, column++));
        row.Children.Add(Column(volume, column++));
        row.Children.Add(Column(remove, column));

        return row;
    }

    /// <summary>
    /// A compact number box. Its height is left to the theme and brought down by
    /// the style on the panel instead — setting it here squashes the frame
    /// without squashing the text box inside the template, and the number
    /// vanishes rather than shrinking.
    /// </summary>
    private static NumericUpDown Number(
        float value, float min, float max, decimal increment, string format, Action<float> apply)
    {
        var box = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = increment,
            FormatString = format,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 5, 0),
            ShowButtonSpinner = false,
            VerticalAlignment = VerticalAlignment.Center,
        };

        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } d) apply((float)d);
        };

        return box;
    }

    /// <summary>
    /// Writes one note back and tells the patch. The row is not rebuilt — the
    /// control the pointer is in has to survive being typed into — so the note
    /// name, which is the one thing showing a value twice, is refreshed by hand.
    /// </summary>
    private void Set(int index, Func<Step, Step> edit)
    {
        if (index >= notes.Count) return;

        var next = edit(notes[index]).Sane();
        if (next == notes[index]) return;

        notes[index] = next;
        Save();

        if (Named
            && rows.Children.Count > index * 2 + 1
            && rows.Children[index * 2 + 1] is Grid row
            && row.Children.FirstOrDefault(c => Grid.GetColumn(c) == 3) is TextBlock name)
        {
            name.Text = spec.AsPort.Format(next.Value);
        }

        changed($"{node.Id} step {index}");
    }

    // --- reordering --------------------------------------------------------------

    /// <summary>
    /// Drag a note to a new place in the list. The row follows the pointer and
    /// the list is left alone until the drag ends: rebuilding it mid-gesture
    /// would destroy the control holding the pointer capture.
    /// </summary>
    private void Reorder(Control handle, Control row, int index)
    {
        handle.PointerPressed += (_, e) =>
        {
            dragging = row;
            dragFrom = index;
            dragTo = index;
            dragOrigin = e.GetPosition(rows);

            e.Pointer.Capture(handle);
            row.ZIndex = 1;
            row.Opacity = 0.8;
        };

        handle.PointerMoved += (_, e) =>
        {
            if (dragging != row) return;

            var moved = e.GetPosition(rows).Y - dragOrigin.Y;

            row.RenderTransform = new TranslateTransform(0, moved);
            dragTo = Math.Clamp(
                index + (int)Math.Round(moved / (RowHeight + InsertHeight)), 0, notes.Count - 1);
        };

        handle.PointerReleased += (_, e) =>
        {
            if (dragging != row) return;

            dragging = null;
            row.RenderTransform = null;
            row.ZIndex = 0;
            row.Opacity = 1;
            e.Pointer.Capture(null);

            if (dragTo == dragFrom) return;

            var moved = notes[dragFrom];
            notes.RemoveAt(dragFrom);
            notes.Insert(dragTo, moved);

            Rebuild();
        };
    }

    // --- small helpers -----------------------------------------------------------

    private static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 9.5,
        Opacity = 0.4,
        VerticalAlignment = VerticalAlignment.Bottom,
    };
}

/// <summary>
/// A level, drawn as how full it is rather than as a thumb on a track. Avalonia
/// has no knob or dial — its only "knob" is the puck inside a ToggleSwitch — and
/// a column of Sliders reads as a column of controls, where a column of these
/// reads as the shape of a pattern.
/// </summary>
internal sealed class LevelBar : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Colors.GridMajor);
    private static readonly IBrush Fill = new SolidColorBrush(Colors.Oscillator);
    private static readonly IBrush Empty = new SolidColorBrush(Colors.Inactive);

    private double level;

    public LevelBar()
    {
        Height = 12;
        MinWidth = 40;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public event Action<double>? ValueChanged;

    public double Value
    {
        get => level;
        set
        {
            var next = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(next - level) < 1e-6) return;

            level = next;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var full = new Rect(Bounds.Size);
        var radius = full.Height / 2;

        context.DrawRectangle(Track, null, new RoundedRect(full, radius));

        if (level <= 0)
        {
            // A rest still shows something, or an empty row looks like a row
            // that failed to draw rather than one deliberately silenced.
            var dot = new Rect(0, 0, full.Height, full.Height).Deflate(3);
            context.DrawEllipse(Empty, null, dot.Center, dot.Width / 2, dot.Height / 2);
            return;
        }

        var width = Math.Max(full.Width * level, full.Height);
        context.DrawRectangle(Fill, null, new RoundedRect(full.WithWidth(width), radius));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Track_(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Equals(e.Pointer.Captured, this)) Track_(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Track_(double x)
    {
        if (Bounds.Width <= 0) return;

        Value = x / Bounds.Width;
        ValueChanged?.Invoke(level);
    }
}
