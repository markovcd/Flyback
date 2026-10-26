using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The card that floats over a module while one of its values turns: the number large
/// enough to read from a step back, a rail of the range with where the value sits on it
/// and where it started, and what the turn has changed.
/// </summary>
/// <remarks>
/// Drawn in screen space, so it reads the same at any zoom, above the module and
/// clear of its rows and wires, or below it where there is no room above.
/// </remarks>
internal static class TurnReadout
{
    public const double Width = 284;
    public const double Height = 164;

    /// <summary>How far the card stands off the module, room for the caret pointing at the well.</summary>
    private const double Gap = 12;

    private const double Corner = 10;
    private const double Margin = 8;

    /// <summary>What one turn shows.</summary>
    /// <param name="Name">The input's name.</param>
    /// <param name="Owner">The module's title.</param>
    /// <param name="Port">The input, which formats its values.</param>
    /// <param name="Value">Where it is now.</param>
    /// <param name="Was">Where it was when the turn began.</param>
    /// <param name="Min">The bottom of the range it turns across.</param>
    /// <param name="Max">The top of it.</param>
    /// <param name="Fine">Whether Shift is held.</param>
    public readonly record struct Turn(string Name, string Owner, PortSpec Port, float Value, float Was, float Min, float Max, bool Fine);

    /// <summary>Where the card goes for a well at <paramref name="well"/> on a module at <paramref name="module"/>, both on screen.</summary>
    public static (Rect Card, bool Above) Place(Rect module, Rect well, Size view)
    {
        var x = Math.Clamp(well.Center.X - Width + 52, Margin, Math.Max(Margin, view.Width - Width - Margin));
        var above = module.Top - Gap - Height >= Margin || module.Bottom + Gap + Height > view.Height - Margin;
        var y = above ? Math.Max(Margin, module.Top - Gap - Height) : module.Bottom + Gap;

        return (new Rect(x, y, Width, Height), above);
    }

    public static void Draw(DrawingContext context, Rect card, bool above, double caretX, Turn turn)
    {
        var (port, min, max) = (turn.Port, turn.Min, turn.Max);
        var share = max > min ? port.Travel(turn.Value, min, max) : 0;
        var start = max > min ? port.Travel(turn.Was, min, max) : 0;

        DrawCaret(context, card, above, caretX);
        context.DrawRectangle(Ground, Edge, card, Corner, Corner);

        // The rail: the range from bottom to top, lit up to the value.
        var rail = new Rect(card.X + 20, card.Y + 14, 4, card.Height - 26);

        context.FillRectangle(Track, rail, 2);
        context.FillRectangle(Attention, new Rect(rail.X, rail.Bottom - rail.Height * share, rail.Width, rail.Height * share), 2);

        foreach (var (at, label) in Ticks(port, min, max))
        {
            var y = rail.Bottom - rail.Height * at;
            var wide = label is not null;

            context.FillRectangle(TickInk, new Rect(rail.Center.X - (wide ? 7 : 4), y - 0.5, wide ? 14 : 8, 1));

            if (label is not null)
            {
                var text = Text(label, 10.5, Quiet, sans: true);
                context.DrawText(text, new Point(rail.X + 16, y - text.Height / 2));
            }
        }

        context.DrawEllipse(null, Ghost, new Point(rail.Center.X, rail.Bottom - rail.Height * start), 7, 7);
        context.DrawEllipse(Brushes.White, Thumb, new Point(rail.Center.X, rail.Bottom - rail.Height * share), 5.25, 5.25);

        // The words, right of the rail.
        var left = card.X + 78;
        var room = card.Right - 14 - left;

        var name = Text(turn.Name, 13, Bright, sans: true, bold: true);
        context.DrawText(name, new Point(left, card.Y + 14));

        var owner = Text(turn.Owner, 11.5, Quiet, sans: true, width: Math.Max(0, room - name.Width - 6 - (turn.Fine ? 40 : 0)));
        context.DrawText(owner, new Point(left + name.Width + 6, card.Y + 14 + (name.Height - owner.Height) / 2));

        if (turn.Fine) DrawBadge(context, new Point(card.Right - 14, card.Y + 14 + name.Height / 2));

        var said = port.Format(turn.Value);
        var number = Text(said, said.Length > 5 || turn.Fine ? 34 : 40, Brushes.White, sans: false, bold: true, width: room);
        context.DrawText(number, new Point(left, card.Y + 34 + (48 - number.Height) / 2));

        var range = Text($"{port.Format(min)} … {port.Format(max)}", 12, Soft, sans: true, width: room);
        context.DrawText(range, new Point(left, card.Y + 84));

        var change = Text(Change(turn), 12, Attention, sans: true, width: room);
        context.DrawText(change, new Point(left, card.Y + 100));

        var hint = Text("drag ↕ · ⇧ fine · esc cancel", 10.5, Hint, sans: true, width: room);
        context.DrawText(hint, new Point(left, card.Bottom - 12 - hint.Height));
    }

    /// <summary>Where the turn began, and by how much it has moved where the number is a plain number.</summary>
    internal static string Change(Turn turn)
    {
        var was = $"was {turn.Port.Format(turn.Was)}";

        if (turn.Port.Display != PortDisplay.Number) return was;

        var by = turn.Value - turn.Was;
        var sign = by < 0 ? "−" : "+";

        return $"{was} · {sign}{turn.Port.Format(Math.Abs(by))}";
    }

    /// <summary>The ends of the range, labeled, the middle of the travel labeled too, and the quarters between.</summary>
    private static IEnumerable<(double At, string? Label)> Ticks(PortSpec port, float min, float max)
    {
        yield return (0, port.Format(min));
        yield return (0.25, null);
        yield return (0.5, port.Format(port.At(0.5, min, max)));
        yield return (0.75, null);
        yield return (1, port.Format(max));
    }

    private static void DrawCaret(DrawingContext context, Rect card, bool above, double x)
    {
        x = Math.Clamp(x, card.X + Corner + 8, card.Right - Corner - 8);
        var tip = above ? card.Bottom + 7 : card.Y - 7;
        var foot = above ? card.Bottom - 1 : card.Y + 1;

        var caret = new StreamGeometry();

        using (var sink = caret.Open())
        {
            sink.BeginFigure(new Point(x - 7, foot), true);
            sink.LineTo(new Point(x, tip));
            sink.LineTo(new Point(x + 7, foot));
            sink.EndFigure(false);
        }

        context.DrawGeometry(Ground, Edge, caret);
    }

    private static void DrawBadge(DrawingContext context, Point rightMiddle)
    {
        var text = Text("FINE", 10, BadgeInk, sans: true, bold: true);
        var box = new Rect(rightMiddle.X - text.Width - 12, rightMiddle.Y - 8, text.Width + 12, 16);

        context.DrawRectangle(Attention, null, box, 3, 3);
        context.DrawText(text, new Point(box.X + 6, box.Center.Y - text.Height / 2));
    }

    private static FormattedText Text(string text, double size, IBrush brush, bool sans, bool bold = false, double width = 0)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            sans ? (bold ? SansBold : Typeface.Default) : (bold ? MonoBold : Mono),
            size,
            brush);

        if (width > 0)
        {
            formatted.MaxTextWidth = width;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    private static readonly Typeface Mono = new(Flyback.Ui.Controls.Text.Mono);
    private static readonly Typeface MonoBold = new(Flyback.Ui.Controls.Text.Mono, weight: FontWeight.SemiBold);
    private static readonly Typeface SansBold = new(Typeface.Default.FontFamily, weight: FontWeight.SemiBold);

    private static readonly IBrush Ground = new ImmutableSolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13), 0.96);
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Avalonia.Media.Colors.White, 0.30));
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5B));
    private static readonly IImmutableBrush Attention = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush TickInk = new ImmutableSolidColorBrush(Colors.Value);
    private static readonly IImmutableBrush Quiet = new ImmutableSolidColorBrush(Color.FromRgb(0x9B, 0xA3, 0xB1));
    private static readonly IBrush Soft = new ImmutableSolidColorBrush(Color.FromRgb(0xB4, 0xBB, 0xC8));
    private static readonly IBrush Hint = new ImmutableSolidColorBrush(Color.FromRgb(0x8F, 0x97, 0xA6));
    private static readonly IBrush Bright = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly IBrush BadgeInk = new ImmutableSolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x04));
    private static readonly IPen Ghost = new ImmutablePen(Quiet, 1.5, new ImmutableDashStyle([2, 2], 0));
    private static readonly IPen Thumb = new ImmutablePen(Attention, 3.5);
}
