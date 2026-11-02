using Avalonia.Media;

namespace Flyback.Ui.Controls;

/// <summary>
/// A color scheme: every ground and every text color in <see cref="Colors"/>, and the
/// accent, under the name the settings save it by. Module accents are the same in all of
/// them, since they say what a module is. <see cref="Light"/> says the grounds are pale and
/// the text dark.
/// </summary>
/// <remarks>Read from a <see cref="SchemeFile"/>, shipped or the user's (<see cref="SchemeLibrary"/>).</remarks>
internal sealed record Scheme(
    string Name,
    Color Edge,
    Color Outline,
    Color Window,
    Color Canvas,
    Color Panel,
    Color Toolbar,
    Color Grid,
    Color GridMajor,
    Color Node,
    Color NodeSelected,
    Color Separator,
    Color Label,
    Color Value,
    Color Normalled,
    Color Muted,
    Color Inactive,
    Color Attention,
    Color Bright,
    bool Light)
{
    /// <summary>The default: blue-gray grounds and the P31 green of the Beam module's trace (ADR-0219).</summary>
    public static Scheme Phosphor { get; } = SchemeLibrary.Shipped[0];
}
