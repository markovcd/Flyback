using System.Globalization;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>How a line of text is laid out on the canvas, and cut to the room it has.</summary>
internal static class CanvasText
{
    /// <summary>The size every row of text on a module or a box is set at.</summary>
    internal const double RowSize = 11.5;

    internal static readonly IBrush LabelBrush = new SolidColorBrush(Colors.Label);
    internal static readonly IBrush ValueBrush = new SolidColorBrush(Colors.Value);

    internal static FormattedText Text(string text, double size, IBrush brush, double maxWidth, bool trim)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        // One line and an ellipsis. A formula has spaces to break at, and wrapped
        // it runs down over the rows below.
        if (trim)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    /// <summary>
    /// A box socket's label as it fits in <paramref name="width"/>, cut in the
    /// middle where it is too long: the port after the last dot is what tells a
    /// box's sockets apart, so it stays.
    /// </summary>
    internal static string Fit(string label, double width)
    {
        var dot = label.LastIndexOf('.');

        if (!Overflows(label, RowSize, width) || dot <= 0) return label;

        var (head, port) = (label[..dot], label[dot..]);
        var (fits, over) = (0, head.Length);

        while (over - fits > 1)
        {
            var mid = (fits + over) / 2;

            if (Overflows(Cut(mid), RowSize, width)) over = mid;
            else fits = mid;
        }

        return Cut(fits);

        string Cut(int keep) => head[..keep].TrimEnd() + "…" + port;
    }

    /// <summary>Whether a label is cut short where it is drawn in <paramref name="width"/>.</summary>
    internal static bool Overflows(string label, double size, double width) =>
        Text(label, size, LabelBrush, width, false).Width > width;
}
