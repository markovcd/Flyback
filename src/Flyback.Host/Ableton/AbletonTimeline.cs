namespace Flyback.Host.Ableton;

/// <summary>
/// A Link session's tempo and where its beats sit, as the wire carries it: the
/// beat <see cref="BeatOrigin"/> fell at ghost time <see cref="TimeOrigin"/>, and
/// beats pass at one per <see cref="MicrosPerBeat"/>.
/// </summary>
/// <param name="MicrosPerBeat">Microseconds a beat.</param>
/// <param name="BeatOrigin">A beat, in millionths.</param>
/// <param name="TimeOrigin">When it fell, in microseconds of the session's ghost time.</param>
internal readonly record struct AbletonTimeline(long MicrosPerBeat, long BeatOrigin, long TimeOrigin)
{
    /// <summary>999 beats a minute, the fastest a session plays.</summary>
    private const long Shortest = 60_060;

    /// <summary>20 beats a minute, the slowest.</summary>
    private const long Longest = 3_000_000;

    /// <summary>Where a session founded here starts: 120 beats a minute, beat nought at ghost time nought.</summary>
    public static AbletonTimeline Founding { get; } = new(500_000, 0, 0);

    public double Bpm => 60e6 / MicrosPerBeat;

    /// <summary>The beat at <paramref name="ghost"/> microseconds of ghost time.</summary>
    public double BeatAt(long ghost) => BeatOrigin / 1e6 + (ghost - TimeOrigin) / (double)MicrosPerBeat;

    /// <summary>The same timeline with its tempo kept within what a session plays.</summary>
    public AbletonTimeline Clamped() => this with { MicrosPerBeat = Math.Clamp(MicrosPerBeat, Shortest, Longest) };
}
