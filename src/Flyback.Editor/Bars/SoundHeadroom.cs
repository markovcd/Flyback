namespace Flyback.Editor.Bars;

/// <summary>A sound's speed read as rungs lit and a <see cref="Headroom"/>.</summary>
internal static class SoundHeadroom
{
    /// <summary>The speed, in times real time, at which each rung lights, lowest first.</summary>
    public static readonly double[] Rungs = [1.1, 1.5, 2, 3, 5];

    /// <summary>Below this the sound is <see cref="Headroom.Tight"/>: one rung lit or none.</summary>
    public const double TightBelow = 1.5;

    /// <summary>How many rungs <paramref name="speed"/> lights.</summary>
    public static int Lit(double speed) => Rungs.Count(rung => speed >= rung);

    /// <summary>What a sound worked out at <paramref name="speed"/> times real time has to spare.</summary>
    /// <param name="speed">Zero while nothing is timed, as when no sound plays.</param>
    public static Headroom Of(double speed, int oversample) =>
        speed <= 0 ? Headroom.Untimed
        : speed < 1 && oversample <= 1 ? Headroom.Behind
        : speed < TightBelow ? Headroom.Tight
        : Headroom.Plenty;

    /// <summary>The word the editor's readout gives for <paramref name="headroom"/>.</summary>
    public static string Word(Headroom headroom) => headroom.ToString().ToLowerInvariant();
}
