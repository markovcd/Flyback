using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Editor.Notices;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The tag in the corner of the view while the text owns the patch (ADR-0068): it says
/// so, names the key that opens the text, and opens it when pressed.
/// </summary>
/// <remarks>Pinned to the view rather than the patch, so no pan or zoom takes it away.</remarks>
internal sealed class OwnerTag(CanvasHistory history, CanvasGlass glass, Viewport view, Reactions reactions) : IReactTo<Touched>
{
    public const string Label = "Text owns this patch";

    public const string Tip =
        "Modules and wires are written in the text, and the canvas is drawn from it. "
        + "Values still turn here, and letting go writes them into the text.\n\n"
        + "Press to open the text.";

    private const string Key = "F2";

    private const double Margin = 12, Height = 28, Padding = 10, Gap = 8, Size = 13, KeySize = 11;

    /// <summary>Whether a finger has been on the screen, which has no F2 to name.</summary>
    private bool fingers;

    public Task On(Touched notice)
    {
        fingers = true;
        return Task.CompletedTask;
    }

    /// <summary>Where the tag stands on screen, or null while the canvas owns the patch or the picture is under it.</summary>
    public Rect? Box
    {
        get
        {
            if (!history.Locked || glass.SeeThrough) return null;

            var width = Padding + Glyph(Colors.Muted).Width + Gap + Text(Colors.Label).Width + Padding;
            if (!fingers) width += Gap + KeyWidth;

            return new Rect(Margin, Margin, Math.Min(width, Math.Max(0, view.Size.Width - 2 * Margin)), Height);
        }
    }

    /// <summary>Whether a point on screen is on the tag.</summary>
    public bool Contains(Point screen) => Box is { } box && box.Contains(screen);

    /// <summary>Opens the text if the press is on the tag, and says whether it was.</summary>
    public bool Press(Point screen)
    {
        if (!Contains(screen)) return false;

        reactions.Raise(new CodeAsked(true));
        return true;
    }

    public void Draw(DrawingContext context)
    {
        if (Box is not { } box) return;

        var muted = Colors.Muted;

        context.DrawRectangle(
            new ImmutableSolidColorBrush(Colors.Panel),
            new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator)),
            box);

        using var clip = context.PushClip(box);

        var glyph = Glyph(muted);
        var x = box.Left + Padding;
        context.DrawText(glyph, new Point(x, box.Center.Y - glyph.Height / 2));
        x += glyph.Width + Gap;

        var text = Text(Colors.Label);
        context.DrawText(text, new Point(x, box.Center.Y - text.Height / 2));
        x += text.Width + Gap;

        if (fingers) return;

        var key = new Rect(x, box.Center.Y - 8, KeyWidth, 16);
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(Colors.Inactive)), key);

        var named = Formatted(Key, KeySize, muted);
        context.DrawText(named, new Point(key.Center.X - named.Width / 2, key.Center.Y - named.Height / 2));
    }

    private const double KeyWidth = 28;

    private static FormattedText Glyph(Color ink) => Formatted("{ }", Size, ink);

    private static FormattedText Text(Color ink) => Formatted(Label, Size, ink);

    private static FormattedText Formatted(string text, double size, Color ink) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, new ImmutableSolidColorBrush(ink));
}
