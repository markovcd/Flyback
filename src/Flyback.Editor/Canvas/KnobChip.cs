using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// What stands in a linked socket's well: the value the panel knob gives it and a
/// chip naming the knob, with the socket drawn as a filled <see cref="SocketDiamond"/>. A click on the
/// chip drops the list of knobs, to pick another or unlink.
/// </summary>
internal static class KnobChip
{
    /// <summary>How far the value keeps off the module's right edge, leaving the chip its room.</summary>
    private const double ValueInset = 40;

    private const double ChipInset = 8, ChipHeight = 16, ChipPadding = 5;

    /// <summary>The knob an unwired socket follows and its place on the panel, or null where it follows none.</summary>
    public static (int Index, PatchControl Knob, ControlLink Link)? Of(Patch patch, NodeInstance node, int port)
    {
        if (patch.IncomingTo(node.Id, port) is not null || ControlMap.Of(node, port) is not { } link || patch.Control(link.Control) is not { } knob)
            return null;

        return (patch.Controls!.IndexOf(knob), knob, link);
    }

    /// <summary>The value and the chip at the right of the row centered on <paramref name="center"/>.</summary>
    public static void Draw(DrawingContext context, Rect bounds, Point center, string value, string tag, IBrush ink)
    {
        var said = Text(value, Mono, 12, ink);
        context.DrawText(said, new Point(bounds.Right - ValueInset - said.Width, center.Y - said.Height / 2));

        var name = Text(tag, MonoBold, 10.5, Light);
        var chip = new Rect(
            bounds.Right - ChipInset - name.Width - 2 * ChipPadding,
            center.Y - ChipHeight / 2,
            name.Width + 2 * ChipPadding,
            ChipHeight);

        context.FillRectangle(ChipFill, chip, 3);
        context.DrawText(name, new Point(chip.Left + ChipPadding, chip.Center.Y - name.Height / 2));
    }

    private static FormattedText Text(string text, Typeface face, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush);

    private static readonly Typeface Mono = new(Flyback.Ui.Controls.Text.Mono);
    private static readonly Typeface MonoBold = new(Flyback.Ui.Controls.Text.Mono, weight: FontWeight.SemiBold);
    private static readonly IBrush ChipFill = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x46));
    private static readonly IBrush Light = new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0));
}
