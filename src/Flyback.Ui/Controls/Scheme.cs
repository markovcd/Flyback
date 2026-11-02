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
    /// <summary>The name of the scheme the editor is drawn in until another is chosen.</summary>
    public const string DefaultName = "Graphite";

    /// <summary>The scheme the editor is drawn in until another is chosen: charcoal, sage text and orange (ADR-0219).</summary>
    public static Scheme Default { get; } = SchemeLibrary.Shipped[0];
}
