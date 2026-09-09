using Avalonia.Controls;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// Every size the shell sets type at, and the one color quiet text is dimmed
/// to — the other half of the theme file.
/// </summary>
/// <remarks>
/// <see cref="Colors"/> gave color a single home and stopped there, so size
/// stayed where XAML would have kept a style: written out again at each of the
/// ninety-odd places that set one. What that costs is not the typing but that
/// nothing holds two captions to the same size, and nothing did — the same
/// small dim line existed at four sizes across five files, which is a
/// difference nobody chose and nobody could see except side by side.
/// <para>
/// Sizes rather than styles, for the reason <see cref="Colors"/> keeps values
/// rather than brushes: a style is a resource with a lifetime and a lookup, and
/// what is actually shared here is a number. A control still says what it is
/// building; it just no longer decides how big.
/// </para>
/// <para>
/// Eight steps, and the gaps between them are meant. Anything closer than half
/// a point apart was the same intention written twice — 10 beside 10.5 and 11.5
/// beside 12 were not distinctions a reader could make, only ones a file could
/// drift into.
/// </para>
/// </remarks>
internal static class Text
{
    // --- the scale, smallest first ------------------------------------------

    /// <summary>
    /// A number that labels something rather than being read: a step's index
    /// above its bar, the octave marks along a keyboard.
    /// </summary>
    public const double Micro = 9.5;

    /// <summary>
    /// A section heading, and the line under a title that says what kind of
    /// thing it was. Small enough that it is read once on the way past.
    /// </summary>
    public const double Caption = 10.5;

    /// <summary>
    /// A note beside a control — what a field means, why a button is off. The
    /// size of everything secondary that is still meant to be read.
    /// </summary>
    public const double Small = 11;

    /// <summary>Everything not otherwise spoken for, and most of the shell.</summary>
    public const double Body = 12;

    /// <summary>One line asked to carry more weight than the body around it.</summary>
    public const double Emphasis = 13;

    /// <summary>A toolbar glyph, and the first line of a dialogue.</summary>
    public const double Heading = 15;

    /// <summary>What the inspector is about: a module's name, a group's.</summary>
    public const double Title = 17;

    /// <summary>The program's own name, which is said once.</summary>
    public const double Display = 22;

    // --- quiet text ---------------------------------------------------------

    /// <summary>
    /// What quiet text is dimmed to, everywhere it is dimmed.
    /// </summary>
    /// <remarks>
    /// A color rather than an opacity, which is the whole of the decision.
    /// Opacity composites against whatever is behind it, so one figure means
    /// four different greys across a toolbar, a panel, a canvas and a flyout —
    /// and the four sites that dimmed this way had drifted to four different
    /// figures trying to match each other through the backgrounds. A foreground
    /// is the same grey wherever it is put.
    /// <para>
    /// A brush here rather than in <see cref="Colors"/>, which keeps values on
    /// purpose. This one is shared because it is always the same brush doing
    /// the same job, which is exactly the case that record leaves to the caller.
    /// </para>
    /// </remarks>
    public static readonly IBrush Muted = new SolidColorBrush(Colors.Muted);

    /// <summary>
    /// Text there to be read once and then ignored.
    /// </summary>
    /// <param name="size">
    /// Which step it is quiet at. <see cref="Small"/> unless a caller says
    /// otherwise, that being what most notes beside a control are.
    /// </param>
    public static TextBlock Quiet(string text, double size = Small) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Muted,
    };
}
