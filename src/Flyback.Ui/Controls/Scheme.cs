using Avalonia.Media;

namespace Flyback.Ui.Controls;

/// <summary>
/// The colors a <see cref="ColorScheme"/> changes: every ground and every text color
/// in <see cref="Colors"/>, and the accent. Module accents are the same in all of them,
/// since they say what a module is.
/// </summary>
internal sealed record Scheme(
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
    Color Attention)
{
    public static Scheme Phosphor { get; } = new(
        Edge: Hex(0x101114),
        Outline: Hex(0x141518),
        Window: Hex(0x16181B),
        Canvas: Hex(0x1A1C20),
        Panel: Hex(0x1C1E22),
        Toolbar: Hex(0x22252A),
        Grid: Hex(0x24272C),
        GridMajor: Hex(0x2C3036),
        Node: Hex(0x2A2D34),
        NodeSelected: Hex(0x32363E),
        Separator: Hex(0x3A3E46),
        Label: Hex(0xC8CCD4),
        Value: Hex(0x8A92A0),
        Normalled: Hex(0x6C7482),
        Muted: Hex(0x8A909A),
        Inactive: Hex(0x5A606A),
        Attention: Hex(0x77EA9A));

    public static Scheme Amber { get; } = new(
        Edge: Hex(0x080F11),
        Outline: Hex(0x0A1214),
        Window: Hex(0x0C1618),
        Canvas: Hex(0x0A1D1E),
        Panel: Hex(0x092121),
        Toolbar: Hex(0x072828),
        Grid: Hex(0x072A2A),
        GridMajor: Hex(0x053433),
        Node: Hex(0x023C3A),
        NodeSelected: Hex(0x004643),
        Separator: Hex(0x195751),
        Label: Hex(0xDDD9BD),
        Value: Hex(0xC9C4AA),
        Normalled: Hex(0x86907B),
        Muted: Hex(0x9BA58C),
        Inactive: Hex(0x626C5E),
        Attention: Hex(0xD1AC00));

    public static Scheme Graphite { get; } = new(
        Edge: Hex(0x161616),
        Outline: Hex(0x1A1A1A),
        Window: Hex(0x202020),
        Canvas: Hex(0x262626),
        Panel: Hex(0x2A2A2A),
        Toolbar: Hex(0x303030),
        Grid: Hex(0x313131),
        GridMajor: Hex(0x3A3A3A),
        Node: Hex(0x363636),
        NodeSelected: Hex(0x404040),
        Separator: Hex(0x4A4B47),
        Label: Hex(0xCBD1BA),
        Value: Hex(0xACBFA4),
        Normalled: Hex(0x7E8979),
        Muted: Hex(0x8E9A88),
        Inactive: Hex(0x646C61),
        Attention: Hex(0xFF7F11));

    public static Scheme Midnight { get; } = new(
        Edge: Hex(0x100C18),
        Outline: Hex(0x130E1D),
        Window: Hex(0x171123),
        Canvas: Hex(0x1A1327),
        Panel: Hex(0x1B1529),
        Toolbar: Hex(0x1E172E),
        Grid: Hex(0x1F182F),
        GridMajor: Hex(0x231B35),
        Node: Hex(0x261E3A),
        NodeSelected: Hex(0x2A2140),
        Separator: Hex(0x3E344E),
        Label: Hex(0xD4C7B7),
        Value: Hex(0xBAA898),
        Normalled: Hex(0x747477),
        Muted: Hex(0x848586),
        Inactive: Hex(0x58575E),
        Attention: Hex(0xF46036));

    /// <summary>The colors a scheme the settings name stands for; one this build does not know is <see cref="Phosphor"/>.</summary>
    public static Scheme Of(ColorScheme scheme) => scheme switch
    {
        ColorScheme.Amber => Amber,
        ColorScheme.Graphite => Graphite,
        ColorScheme.Midnight => Midnight,
        _ => Phosphor,
    };

    private static Color Hex(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
