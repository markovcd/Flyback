using System.Net;

namespace Flyback.Host.Ableton;

/// <summary>Another app on the network, as of its latest announcement.</summary>
/// <param name="Node">Which app.</param>
/// <param name="Session">The session it is in.</param>
/// <param name="Timeline">Its session's timeline.</param>
/// <param name="StartStop">Its session's transport.</param>
/// <param name="MeasuredAt">Where it answers pings, or null where it said nowhere this side can reach.</param>
/// <param name="Expires">When it is forgotten unless it announces itself again, on this side's clock.</param>
internal sealed record AbletonPeer(
    ulong Node,
    ulong Session,
    AbletonTimeline Timeline,
    AbletonStartStop StartStop,
    IPEndPoint? MeasuredAt,
    long Expires);
