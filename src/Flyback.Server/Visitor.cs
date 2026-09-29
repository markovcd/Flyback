using System.Net;
using System.Net.Sockets;

namespace Flyback.Server;

/// <summary>Who a request counts as, for the limiters and for one rating each.</summary>
/// <remarks>An IPv6 address counts as its /64, since one household is handed a whole /64 and could use a fresh address per request.</remarks>
internal static class Visitor
{
    public static string Of(IPAddress? address)
    {
        if (address is null) return "unknown";

        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        bytes.AsSpan(8).Clear();

        return new IPAddress(bytes) + "/64";
    }
}
