using System.Net;

namespace Flyback.Host.Ableton;

/// <summary>One app's announcement of itself on the multicast group, read off the wire.</summary>
/// <param name="Kind">One of <see cref="AbletonWire.Alive"/>, <see cref="AbletonWire.Response"/> and <see cref="AbletonWire.ByeBye"/>.</param>
/// <param name="Ttl">Seconds to believe it for without hearing from it again.</param>
/// <param name="Node">Which app.</param>
/// <param name="Session">The session it is in. Null in a goodbye.</param>
/// <param name="Timeline">Its session's timeline. Null in a goodbye.</param>
/// <param name="StartStop">Its session's transport, or nought where the app sends none.</param>
/// <param name="MeasuredAt">Where it answers pings, or null where it said nowhere this side can reach.</param>
internal sealed record AbletonMessage(
    byte Kind,
    byte Ttl,
    ulong Node,
    ulong? Session,
    AbletonTimeline? Timeline,
    AbletonStartStop StartStop,
    IPEndPoint? MeasuredAt);
