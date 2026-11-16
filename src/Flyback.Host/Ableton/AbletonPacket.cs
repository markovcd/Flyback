using System.Net;

namespace Flyback.Host.Ableton;

/// <summary>A message for one app, waiting to be sent.</summary>
internal readonly record struct AbletonPacket(byte[] Bytes, IPEndPoint To);
