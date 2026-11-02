using Avalonia.Media;

namespace Flyback.Ui.Controls;

/// <summary>Mixing one color into another, with no scheme behind it.</summary>
/// <remarks>
/// Apart from <see cref="Colors"/> because the shipped schemes are read while
/// <see cref="Colors"/> is being set up: a call into it from there is a cycle, which an
/// ahead-of-time WebAssembly build resolves by failing to start.
/// </remarks>
internal static class ColorMix
{
    /// <summary>
    /// <paramref name="amount"/> of <paramref name="over"/> laid on
    /// <paramref name="ground"/>, opaque.
    /// </summary>
    public static Color Blend(Color ground, Color over, double amount) => Color.FromRgb(
        Part(ground.R, over.R, amount),
        Part(ground.G, over.G, amount),
        Part(ground.B, over.B, amount));

    /// <summary>The share <paramref name="amount"/> of the way from one channel value to another.</summary>
    public static byte Part(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);
}
