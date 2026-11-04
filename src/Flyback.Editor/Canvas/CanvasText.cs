using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>How a line of text is laid out on the canvas, and cut to the room it has.</summary>
internal static class CanvasText
{
    /// <summary>The size every row of text on a module or a box is set at.</summary>
    internal const double RowSize = 11.5;

    internal static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Colors.Label);
    internal static readonly IBrush ValueBrush = new ImmutableSolidColorBrush(Colors.Value);

    /// <remarks>The same text, size, brush and room give back the same object, so it must not be changed once made.</remarks>
    internal static FormattedText Text(string text, double size, IBrush brush, double maxWidth, bool trim) =>
        Laid.Get((text, size, brush, maxWidth, trim), static key => Lay(key.Text, key.Size, key.Brush, key.MaxWidth, key.Trim));

    private static FormattedText Lay(string text, double size, IBrush brush, double maxWidth, bool trim)
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
        // Measuring is text shaping, a dozen times over for a long label.
        return Fitted.Get((label, width), static key => Cut(key.Label, key.Width));
    }

    private static readonly Remembered<(string Label, double Width), string> Fitted = new(4096);

    /// <summary>
    /// Draws text made by <see cref="Text"/> at <paramref name="at"/>. Drawing a
    /// <see cref="FormattedText"/> shapes it again every time, so its glyphs are kept
    /// and drawn from instead.
    /// </summary>
    internal static void Draw(DrawingContext context, FormattedText text, Point at)
    {
        var glyphs = Glyphs.GetValue(text, static laid =>
        {
            var drawing = new DrawingGroup();

            using (var into = drawing.Open()) into.DrawText(laid, default);

            return drawing;
        });

        using (context.PushTransform(Matrix.CreateTranslation(at.X, at.Y))) glyphs.Draw(context);
    }

    private static readonly ConditionalWeakTable<FormattedText, DrawingGroup> Glyphs = [];

    /// <summary>Laid-out text, which is shaped once and drawn again on every frame it stays the same.</summary>
    private static readonly Remembered<(string Text, double Size, IBrush Brush, double MaxWidth, bool Trim), FormattedText> Laid = new(4096);

    private static string Cut(string label, double width)
    {
        var dot = label.LastIndexOf('.');

        if (dot <= 0 || !Overflows(label, RowSize, width)) return label;

        var (head, port) = (label[..dot], label[dot..]);
        var (fits, over) = (0, head.Length);

        while (over - fits > 1)
        {
            var mid = (fits + over) / 2;

            if (Overflows(Keep(mid), RowSize, width)) over = mid;
            else fits = mid;
        }

        return Keep(fits);

        string Keep(int keep) => head[..keep].TrimEnd() + "…" + port;
    }

    /// <summary>Whether a label is cut short where it is drawn in <paramref name="width"/>.</summary>
    internal static bool Overflows(string label, double size, double width) =>
        Text(label, size, LabelBrush, width, false).Width > width;
}
