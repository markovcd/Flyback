namespace Flyback.Host.Ableton;

/// <summary>Whether a Link session's transport is playing, as of the latest app to press Start or Stop.</summary>
/// <param name="Playing">Whether it is playing.</param>
/// <param name="Beats">The beat it started or stopped on, in millionths.</param>
/// <param name="Timestamp">
/// When, in microseconds of ghost time. Nought when nobody in the session has
/// pressed either, which is every session whose apps leave start and stop alone.
/// </param>
internal readonly record struct AbletonStartStop(bool Playing, long Beats, long Timestamp);
