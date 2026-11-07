using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Sequence;

/// <summary>
/// The Song's parts drawn as rows, each its own length with its own playhead, under a
/// ruler of sections. A click switches a level, a double-click makes it glide, a drag up
/// or down turns it, a drag along paints it, and a part's handle sets how long it loops.
/// </summary>
/// <remarks>Drawn rather than built of controls, because a row holds up to 256 sections.</remarks>
internal sealed class SongRows : Control
{
    /// <summary>How far a drag goes for a part's whole range, as a knob's does; Shift makes it five times finer.</summary>
    private const double Travel = 160;

    /// <summary>How far a mouse moves before a press is a drag rather than a click.</summary>
    private const double Slop = 3;

    /// <summary>How long the rows hold still after somebody scrolls them, before following the playhead again.</summary>
    private static readonly TimeSpan HoldAfterScroll = TimeSpan.FromSeconds(3);

    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly Typeface MonoFace = new(Text.Mono);

    private SongParts parts = new([]);
    private Press? press;
    private DateTime scrolledAt = DateTime.MinValue;

    /// <summary>The cell a click last switched and what it was, which a double-click puts back before it glides.</summary>
    private (int Part, int Section, PartLevel Before)? clicked;

    public SongRows()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public RowLayout Layout { get; } = new();

    /// <summary>The parts drawn; set again whenever the Song changes.</summary>
    public SongParts Parts
    {
        get => parts;
        set
        {
            parts = value;
            Layout.Content = (parts.Count, parts.Longest);
            Layout.ScrollBy(0);
            InvalidateVisual();
        }
    }

    /// <summary>How many sections the Song has played, which places every part's playhead.</summary>
    public double Played
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Follow();
            InvalidateVisual();
        }
    }

    /// <summary>The Song's accent, which a level is shaded in.</summary>
    public Color Accent { get; set; } = Colors.Label;

    /// <summary>Drawn as one strip of thin lines, with nothing to press but the strip.</summary>
    public bool Mini
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether a press is under way, whose edits are not yet on the Song.</summary>
    public bool Pressed => press is not null;

    /// <summary>A level, a length or a part changed, filed under what it is.</summary>
    public event Action<string?>? Edited;

    /// <summary>The ruler was pressed at a section.</summary>
    public event Action<double>? Sought;

    /// <summary>A part's name was double-clicked, to be renamed in a box over it.</summary>
    public event Action<int, Rect>? RenameAsked;

    /// <summary>A part's loop chip was clicked, to have its length typed in a box over it.</summary>
    public event Action<int, Rect>? LengthAsked;

    /// <summary>A part's name was pressed with the right button.</summary>
    public event Action<int>? MenuAsked;

    /// <summary>The collapsed strip was pressed.</summary>
    public event Action? Opened;

    /// <summary>Where a part's loop chip stands in the names column.</summary>
    public Rect Chip(int part)
    {
        var top = Layout.RowTop(part);
        return new Rect(RowLayout.NameWidth - 42, top + (Layout.Row - 16) / 2, 36, 16);
    }

    /// <summary>Where a part's name stands in the names column.</summary>
    public Rect NameBox(int part) => new(8, Layout.RowTop(part) + (Layout.Row - 22) / 2, RowLayout.NameWidth - 54, 22);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout.Size = finalSize;
        Layout.ScrollBy(0);
        return base.ArrangeOverride(finalSize);
    }

    // --- drawing --------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        Layout.Size = Bounds.Size;

        context.FillRectangle(new SolidColorBrush(Colors.Panel), new Rect(Bounds.Size));

        if (Mini)
        {
            DrawMini(context);
            return;
        }

        DrawRuler(context);

        using (context.PushClip(new Rect(RowLayout.NameWidth, 0, Layout.Window, Bounds.Height)))
        {
            for (var p = 0; p < parts.Count; p++) DrawRow(context, p);
        }

        DrawNames(context);
        DrawBar(context);
    }

    private void DrawRuler(DrawingContext context)
    {
        var faint = new Pen(new SolidColorBrush(Colors.Grid), 1);
        var bar = new Pen(new SolidColorBrush(Colors.GridMajor), 1);
        var label = new SolidColorBrush(Colors.Muted);

        context.FillRectangle(new SolidColorBrush(Colors.Canvas), new Rect(RowLayout.NameWidth, 0, Layout.Window, RowLayout.RulerHeight));

        var (first, last) = Visible(parts.Longest + 1);
        var every = Layout.Cell >= 16 ? 4 : Layout.Cell >= 8 ? 8 : 16;

        using (context.PushClip(new Rect(RowLayout.NameWidth, 0, Layout.Window, RowLayout.RulerHeight)))
        {
            for (var s = first; s <= last; s++)
            {
                var x = Math.Round(Layout.X(s)) + 0.5;
                var marked = s % every == 0;

                context.DrawLine(marked ? bar : faint, new Point(x, marked ? 0 : RowLayout.RulerHeight - 7), new Point(x, RowLayout.RulerHeight));

                if (marked) context.DrawText(Words(s + 1, MonoFace, label, 10), new Point(x + 4, 4));
            }
        }

        context.DrawLine(faint, new Point(RowLayout.NameWidth, RowLayout.RulerHeight - 0.5), new Point(Bounds.Width, RowLayout.RulerHeight - 0.5));
    }

    private void DrawRow(DrawingContext context, int part)
    {
        var levels = parts.Levels(part);
        var top = Layout.RowTop(part);
        var row = Layout.Row;
        var inset = Math.Min(5, row / 6);
        var gap = Layout.Cell >= 10 ? 1 : 0;

        var (first, last) = Visible(levels.Count - 1);

        for (var s = first; s <= last; s++)
        {
            var now = levels[s];
            var was = levels[(s + levels.Count - 1) % levels.Count];
            var cell = new Rect(Layout.X(s) + gap, top + inset, Math.Max(1, Layout.Cell - 2 * gap), row - 2 * inset);

            IBrush fill = now.Glides
                ? new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Shade(was.Value), 0), new GradientStop(Shade(now.Value), 1) },
                }
                : new SolidColorBrush(Shade(now.Value));

            context.DrawRectangle(fill, null, cell, 2, 2);
        }

        var head = Played % levels.Count;
        var at = Math.Floor(head);
        var glow = new SolidColorBrush(Colors.Attention);

        context.DrawRectangle(null, new Pen(glow, 2), new Rect(Layout.X(at), top + inset - 2, Layout.Cell, row - 2 * inset + 4), 3, 3);
        context.FillRectangle(glow, new Rect(Layout.X(head) - 1, top, 2, row));

        var end = Layout.X(levels.Count);
        context.FillRectangle(new SolidColorBrush(Colors.Label), new Rect(end - 2.5, top + inset - 2, 5, row - 2 * inset + 4));

        context.DrawLine(new Pen(new SolidColorBrush(Colors.Separator), 1), new Point(RowLayout.NameWidth, top + row - 0.5), new Point(Bounds.Width, top + row - 0.5));
    }

    private void DrawNames(DrawingContext context)
    {
        var column = new Rect(0, 0, RowLayout.NameWidth, Bounds.Height);
        context.FillRectangle(new SolidColorBrush(Colors.Panel), column);
        context.DrawLine(new Pen(new SolidColorBrush(Colors.Edge), 1), new Point(RowLayout.NameWidth - 0.5, 0), new Point(RowLayout.NameWidth - 0.5, Bounds.Height));

        var ink = new SolidColorBrush(Colors.Label);
        var muted = new SolidColorBrush(Colors.Muted);
        var outline = new Pen(new SolidColorBrush(Colors.Outline), 1);

        for (var p = 0; p < parts.Count; p++)
        {
            var name = Words(parts.Name(p), Face, ink, 12, NameBox(p).Width);
            var box = NameBox(p);
            context.DrawText(name, new Point(box.X, box.Center.Y - name.Height / 2));

            var chip = Chip(p);
            context.DrawRectangle(null, outline, chip, 3, 3);

            var length = Words("↻" + parts.Levels(p).Count.ToString(CultureInfo.InvariantCulture), MonoFace, muted, 10);
            context.DrawText(length, new Point(chip.Center.X - length.Width / 2, chip.Center.Y - length.Height / 2));
        }
    }

    private void DrawBar(DrawingContext context)
    {
        if (Layout.Thumb is not { } thumb) return;

        context.FillRectangle(new SolidColorBrush(Colors.Canvas), new Rect(RowLayout.NameWidth, Bounds.Height - RowLayout.BarHeight, Layout.Window, RowLayout.BarHeight));
        context.DrawRectangle(new SolidColorBrush(Colors.Outline), null, thumb, 3, 3);
    }

    /// <summary>Every part as a thin line of its own length across the strip, each with its playhead.</summary>
    private void DrawMini(DrawingContext context)
    {
        var label = Words("SEQUENCE", Face, new SolidColorBrush(Colors.Muted), 10);
        context.DrawText(label, new Point(12, Bounds.Height / 2 - label.Height / 2));

        if (parts.Count == 0) return;

        var width = Bounds.Width - RowLayout.NameWidth - 12;
        var cell = width / Math.Max(1, parts.Longest);
        var line = Math.Clamp((Bounds.Height - 16) / parts.Count - 3, 2, 5);
        var glow = new SolidColorBrush(Colors.Attention);

        for (var p = 0; p < parts.Count; p++)
        {
            var levels = parts.Levels(p);
            var top = 8 + p * (line + 3);

            for (var s = 0; s < levels.Count; s++)
                context.FillRectangle(new SolidColorBrush(Shade(levels[s].Value)), new Rect(RowLayout.NameWidth + s * cell, top, Math.Max(1, cell - 1), line));

            context.FillRectangle(glow, new Rect(RowLayout.NameWidth + Played % levels.Count * cell, top - 1, 2, line + 2));
        }
    }

    /// <summary>The sections from 0 to <paramref name="last"/> that are in view.</summary>
    private (int First, int Last) Visible(int last) =>
        (Math.Max(0, (int)Math.Floor(Layout.Section(RowLayout.NameWidth))), Math.Min(last, (int)Math.Ceiling(Layout.Section(Bounds.Width))));

    /// <summary>A level's shade: nought is the unlit cell and one or more the full accent; below nought, the accent turned half-way round.</summary>
    private Color Shade(float level)
    {
        var over = Accent;

        if (level < 0f)
        {
            var hsl = Accent.ToHsl();
            over = new HslColor(hsl.A, (hsl.H + 180) % 360, hsl.S, hsl.L).ToRgb();
        }

        return Colors.Blend(Colors.Node, over, Math.Clamp(Math.Abs(level), 0f, 1f));
    }

    private static FormattedText Words(object text, Typeface face, IBrush ink, double size, double width = double.PositiveInfinity)
    {
        var formatted = new FormattedText(Convert.ToString(text, CultureInfo.InvariantCulture) ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, ink);
        if (double.IsFinite(width)) formatted.MaxTextWidth = Math.Max(1, width);
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }

    // --- following -------------------------------------------------------------

    /// <summary>
    /// Keeps the longest part's playhead in view, unless the pointer is over the rows or
    /// somebody scrolled them a moment ago, so the cells under the pointer hold still.
    /// </summary>
    private void Follow()
    {
        if (Mini || parts.Count == 0 || IsPointerOver || press is not null || DateTime.UtcNow - scrolledAt < HoldAfterScroll) return;

        var head = Layout.X(Played % parts.Longest);
        if (head >= RowLayout.NameWidth + Layout.Window * 0.1 && head <= RowLayout.NameWidth + Layout.Window * 0.85) return;

        Layout.ScrollTo(Played % parts.Longest * Layout.Cell - Layout.Window * 0.25);
    }

    /// <summary>Scrolls by hand, which holds following off for a moment.</summary>
    public void ScrollBy(double pixels)
    {
        scrolledAt = DateTime.UtcNow;
        Layout.ScrollBy(pixels);
        InvalidateVisual();
    }

    // --- gestures ---------------------------------------------------------------

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Mini) return;

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            Layout.Zoom(Layout.Cell * Math.Pow(1.15, e.Delta.Y), e.GetPosition(this).X);
            scrolledAt = DateTime.UtcNow;
            InvalidateVisual();
        }
        else
        {
            var along = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            ScrollBy(-along * Layout.Cell * 2);
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var at = e.GetPosition(this);
        var point = e.GetCurrentPoint(this).Properties;

        if (Mini)
        {
            Opened?.Invoke();
            e.Handled = true;
            return;
        }

        var part = Layout.PartAt(at.Y);

        if (at.X < RowLayout.NameWidth)
        {
            if (part < 0) return;

            if (point.IsRightButtonPressed) MenuAsked?.Invoke(part);
            else if (Chip(part).Inflate(2).Contains(at)) LengthAsked?.Invoke(part, Chip(part));
            else if (e.ClickCount == 2) RenameAsked?.Invoke(part, NameBox(part));

            e.Handled = true;
            return;
        }

        if (!point.IsLeftButtonPressed) return;

        if (at.Y >= Bounds.Height - RowLayout.BarHeight && Layout.Thumb is { } thumb)
        {
            if (!thumb.Contains(at)) Layout.ScrollTo(Layout.ScrollForThumb(at.X - thumb.Width / 2));

            scrolledAt = DateTime.UtcNow;
            Begin(e, new Press(Gesture.Thumb, -1, -1, at, Layout.Scroll - Layout.ScrollForThumb(at.X)));
            InvalidateVisual();
            return;
        }

        if (at.Y < RowLayout.RulerHeight)
        {
            Sought?.Invoke(Math.Max(0, Math.Floor(Layout.Section(at.X))));
            e.Handled = true;
            return;
        }

        if (part < 0) return;

        var levels = parts.Levels(part);

        if (Math.Abs(at.X - Layout.X(levels.Count)) <= RowLayout.HandleReach)
        {
            Begin(e, new Press(Gesture.Length, part, levels.Count, at, 0));
            return;
        }

        var section = (int)Math.Floor(Layout.Section(at.X));
        if (section < 0 || section >= levels.Count) return;

        if (e.ClickCount == 2 && clicked is { } first && first.Part == part && first.Section == section)
        {
            parts.Glide(part, section, first.Before);
            clicked = null;
            Changed(Because(part, section) + " click");
            e.Handled = true;
            return;
        }

        Begin(e, new Press(e.Pointer.Type == PointerType.Touch ? Gesture.Finger : Gesture.Undecided, part, section, at, levels[section].Value)
        {
            High = Math.Max(1f, levels.Max(level => Math.Abs(level.Value))),
        });
    }

    private void Begin(PointerPressedEventArgs e, Press held)
    {
        press = held;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (press is not { } held) return;

        var at = e.GetPosition(this);
        var moved = at - held.From;

        switch (held.Gesture)
        {
            case Gesture.Undecided when Math.Max(Math.Abs(moved.X), Math.Abs(moved.Y)) >= Slop:
                held.Gesture = Math.Abs(moved.X) > Math.Abs(moved.Y) ? Gesture.Paint : Gesture.Turn;
                OnPointerMoved(e);
                return;

            case Gesture.Finger when Math.Max(Math.Abs(moved.X), Math.Abs(moved.Y)) >= FingerSwipe.Slop:
                held.Gesture = Math.Abs(moved.X) > Math.Abs(moved.Y) ? Gesture.Pan : Gesture.Turn;
                held.Last = at;
                return;

            case Gesture.Turn:
                Turn(held, at.Y, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                break;

            case Gesture.Paint:
                parts.Paint(held.Part, held.Section, (int)Math.Floor(Layout.Section(at.X)), parts.Levels(held.Part)[held.Section]);
                held.Moved = true;
                break;

            case Gesture.Length:
                parts.Resize(held.Part, (int)Math.Round(Layout.Section(at.X)));
                Layout.Content = (parts.Count, parts.Longest);
                held.Moved = true;
                break;

            case Gesture.Pan:
                Layout.ScrollBy(held.Last.X - at.X);
                held.Last = at;
                scrolledAt = DateTime.UtcNow;
                break;

            case Gesture.Thumb:
                Layout.ScrollTo(Layout.ScrollForThumb(at.X) + held.From0);
                scrolledAt = DateTime.UtcNow;
                break;

            default:
                return;
        }

        InvalidateVisual();
    }

    private void Turn(Press held, double y, bool fine)
    {
        var rise = held.From.Y - y;
        var perPixel = held.High / (Travel * (fine ? 5d : 1d));
        var turned = Math.Clamp(held.From0 + (float)(rise * perPixel), -held.High, held.High);
        var grain = held.High <= 2f ? 0.01f : 0.1f;

        parts.Set(held.Part, held.Section, MathF.Round((float)turned / grain) * grain);
        held.Moved = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (press is not { } held) return;

        press = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (held.Moved)
        {
            Changed(held.Gesture switch
            {
                Gesture.Length => $"part {held.Part} length",
                Gesture.Paint => $"part {held.Part} paint",
                _ => Because(held.Part, held.Section),
            });
            return;
        }

        if (held.Gesture is Gesture.Undecided or Gesture.Finger)
        {
            clicked = (held.Part, held.Section, parts.Levels(held.Part)[held.Section]);
            parts.Toggle(held.Part, held.Section);
            Changed(Because(held.Part, held.Section) + " click");
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (press is not { Moved: true } held) return;

        press = null;
        Changed(Because(held.Part, held.Section));
    }

    private static string Because(int part, int section) => $"song part {part} section {section}";

    private void Changed(string because)
    {
        Layout.Content = (parts.Count, parts.Longest);
        InvalidateVisual();
        Edited?.Invoke(because);
    }

    /// <summary>The level under a point, for its tooltip.</summary>
    public string? Describe(Point at)
    {
        var part = Layout.PartAt(at.Y);
        if (part < 0 || at.X < RowLayout.NameWidth) return null;

        var section = (int)Math.Floor(Layout.Section(at.X));
        if (section < 0 || section >= parts.Levels(part).Count) return null;

        return $"{parts.Name(part)}, section {section + 1}: {ArrangementNotation.Row([parts.Levels(part)[section]])}";
    }

    private enum Gesture
    {
        Undecided,
        Finger,
        Turn,
        Paint,
        Length,
        Pan,
        Thumb,
    }

    /// <summary>A press under way: what it does, where it began, and the level it began at.</summary>
    private sealed class Press(Gesture gesture, int part, int section, Point from, double from0)
    {
        public Gesture Gesture { get; set; } = gesture;
        public int Part { get; } = part;
        public int Section { get; } = section;
        public Point From { get; } = from;

        /// <summary>The level it began at, or for the scroll bar the scroll's offset from the thumb.</summary>
        public double From0 { get; } = from0;

        public float High { get; init; } = 1f;
        public Point Last { get; set; } = from;
        public bool Moved { get; set; }
    }
}
