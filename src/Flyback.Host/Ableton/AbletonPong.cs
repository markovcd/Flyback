namespace Flyback.Host.Ableton;

/// <summary>An answer to a ping, read off the wire. A time that was not sent is nought.</summary>
/// <param name="Session">The session of the app that answered.</param>
/// <param name="Ghost">Its ghost time as it answered.</param>
/// <param name="PreviousGhost">The ghost time of its answer before, echoed from the ping.</param>
/// <param name="Host">This side's clock as the ping left, echoed from the ping.</param>
internal readonly record struct AbletonPong(ulong Session, long Ghost, long PreviousGhost, long Host);
