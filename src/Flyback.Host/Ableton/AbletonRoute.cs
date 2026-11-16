using System.Net;
using System.Net.Sockets;

namespace Flyback.Host.Ableton;

/// <summary>One network this side is on: the socket it announces, answers and pings from there, and the network's mask.</summary>
internal sealed record AbletonRoute(Socket Socket, IPEndPoint Local, IPAddress Mask)
{
    /// <summary>Whether <paramref name="remote"/> is on this network.</summary>
    public bool Faces(IPAddress remote)
    {
        if (remote.AddressFamily != AddressFamily.InterNetwork) return false;

        Span<byte> mine = stackalloc byte[4], theirs = stackalloc byte[4], mask = stackalloc byte[4];

        Local.Address.TryWriteBytes(mine, out _);
        remote.TryWriteBytes(theirs, out _);
        Mask.TryWriteBytes(mask, out _);

        for (var i = 0; i < 4; i++)
            if ((mine[i] & mask[i]) != (theirs[i] & mask[i])) return false;

        return true;
    }
}
