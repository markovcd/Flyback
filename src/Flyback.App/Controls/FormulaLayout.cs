using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// An Expression's formula written out in its body, beside the socket letters.
/// The header is the module's name, as for any other module.
/// </summary>
internal static class FormulaLayout
{
    private const double FormulaSize = 11;

    /// <summary>Where the formula starts, clear of the socket letters.</summary>
    private const double FormulaInset = 30;

    /// <summary>
    /// Whether <paramref name="formula"/> reads socket <paramref name="socket"/>.
    /// A socket is its one letter standing alone, since every function's name is
    /// longer than that.
    /// </summary>
    internal static bool Reads(string formula, int socket) =>
        socket < SocketLetters.Length
        && Regex.IsMatch(formula, $@"(?<![A-Za-z0-9_]){SocketLetters[socket]}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase);

    /// <summary>An Expression's sockets, in socket order.</summary>
    private const string SocketLetters = "abcd";

    /// <summary>
    /// The formula as it is drawn in the body, where that is, and whether it is
    /// cut short. Null for any module but an Expression.
    /// </summary>
    internal static (FormattedText Text, Point At, Rect Area, bool Cut)? FormulaBlock(
        Patch patch, NodeInstance node, NodeDef def, Rect bounds)
    {
        if (NodeCatalog.FormulaOf(node) is not { } formula || string.IsNullOrWhiteSpace(formula)) return null;

        var reserve = Reserve(patch, node, def, bounds, formula);

        var area = new Rect(
            bounds.X + FormulaInset,
            bounds.Y + NodeGeometry.HeaderHeight + 3,
            Math.Max(0, bounds.Width - FormulaInset - reserve),
            Math.Max(0, bounds.Height - NodeGeometry.HeaderHeight - NodeGeometry.FooterPadding - 2));

        var whole = Wrapped(formula.Trim(), area.Width, lines: 0);
        var lineHeight = Wrapped("a", area.Width, lines: 0).Height;
        var lines = Math.Max(1, (int)(area.Height / lineHeight));
        var cut = whole.Height > lines * lineHeight + 0.5;
        var text = cut ? Wrapped(formula.Trim(), area.Width, lines) : whole;

        // Level with the rows when it is short, so a one-line formula sits beside
        // the output rather than floating in the middle of the body.
        return (text, new Point(area.X, area.Y + 2), area, cut);
    }

    /// <summary>
    /// How much of the right of the body the formula keeps clear of: the output's
    /// name, and the value of each knob the formula reads.
    /// </summary>
    private static double Reserve(Patch patch, NodeInstance node, NodeDef def, Rect bounds, string formula)
    {
        var reserve = 0d;

        foreach (var port in def.Outputs)
            reserve = Math.Max(reserve, CanvasText.Text(port.Name, 11.5, CanvasText.LabelBrush, bounds.Width - 24, true).Width + 22);

        for (var i = 0; i < def.Inputs.Count && i < node.InputValues.Length; i++)
        {
            if (patch.IncomingTo(node.Id, i) is not null || ControlMap.Of(node, i) is not null || !Reads(formula, i)) continue;

            var value = CanvasText.Text(def.Inputs[i].Format(node.InputValues[i]), 11.5, CanvasText.ValueBrush, bounds.Width * 0.4, true);
            reserve = Math.Max(reserve, value.Width + 20);
        }

        return reserve;
    }

    private static FormattedText Wrapped(string text, double width, int lines)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            FormulaSize,
            CanvasText.ValueBrush) { MaxTextWidth = width };

        if (lines > 0)
        {
            formatted.MaxLineCount = lines;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }
}
