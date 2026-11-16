namespace Flyback.Host.Ableton;

/// <summary>What the Link session looks like from here, for a command to print.</summary>
/// <param name="Session">The session's id, in hex.</param>
/// <param name="Peers">How many other apps are in it.</param>
/// <param name="Bpm">Its tempo.</param>
/// <param name="Beat">Its beat, counted from the bar line found on joining.</param>
/// <param name="Running">Whether its transport is running.</param>
internal sealed record AbletonView(string Session, int Peers, double Bpm, double Beat, bool Running)
{
    public static AbletonView Apart { get; } = new("", 0, 0d, 0d, false);
}
