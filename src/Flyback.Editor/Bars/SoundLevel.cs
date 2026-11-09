namespace Flyback.Editor.Bars;

/// <summary>A peak read as decibels below full scale, and where it falls on the status bar's level bar.</summary>
internal static class SoundLevel
{
    /// <summary>The quietest level the bar shows; anything below draws nothing.</summary>
    public const double Floor = -48;

    /// <summary>Within this many dB of full scale the level is hot, drawn in the warning color.</summary>
    public const double HotWithin = 3;

    /// <summary>A peak, 1 being full scale, in dB; silence is negative infinity.</summary>
    public static double Decibels(float peak) => peak > 0 ? 20 * Math.Log10(peak) : double.NegativeInfinity;

    /// <summary>How far along the bar <paramref name="decibels"/> reaches, 0 to 1, even in dB.</summary>
    public static double Reach(double decibels) => Math.Clamp((decibels - Floor) / -Floor, 0, 1);

    /// <summary>Whether <paramref name="decibels"/> is close enough to full scale to warn of.</summary>
    public static bool Hot(double decibels) => decibels > -HotWithin;
}
