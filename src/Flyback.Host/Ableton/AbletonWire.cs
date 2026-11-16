using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Flyback.Host.Ableton;

/// <summary>
/// Ableton Link's two kinds of message, written and read: an app announcing its
/// session on the multicast group, and the ping and pong that measure one
/// app's clock against another's.
/// </summary>
/// <remarks>
/// Big-endian throughout. Each message is eight bytes naming the protocol, a
/// header, and a payload of entries, each a four-letter key, a length and a
/// value. A reader skips keys it does not know, so a newer app's extra entries
/// pass by. What arrives is from anyone on the network, so a reader never
/// throws: a length that runs past the packet, or a value too short for its
/// key, rejects the whole message.
/// </remarks>
internal static class AbletonWire
{
    /// <summary>Where every app announces itself.</summary>
    public static IPEndPoint Group { get; } = new(new IPAddress([224, 76, 78, 75]), 20808);

    /// <summary>The longest message either side sends or reads.</summary>
    public const int Largest = 512;

    /// <summary>Seconds an announcement holds for.</summary>
    public const byte Ttl = 5;

    public const byte Alive = 1;

    public const byte Response = 2;

    public const byte ByeBye = 3;

    private const byte Ping = 1;

    private const byte Pong = 2;

    private const uint TimelineKey = 0x746d6c6e;    // tmln

    private const uint SessionKey = 0x73657373;     // sess

    private const uint StartStopKey = 0x73747374;   // stst

    private const uint EndpointKey = 0x6d657034;    // mep4

    private const uint HostKey = 0x5f5f6874;        // __ht

    private const uint GhostKey = 0x5f5f6774;       // __gt

    private const uint PreviousGhostKey = 0x5f706774; // _pgt

    /// <summary>A ping's payload holds a host time and a previous ghost time at most.</summary>
    private const int LongestPing = 2 * (8 + 8);

    private static ReadOnlySpan<byte> Discovery => "_asdp_v\u0001"u8;

    private static ReadOnlySpan<byte> Measurement => "_link_v\u0001"u8;

    /// <summary>An app's announcement: its session, timeline and transport, and where it answers pings.</summary>
    public static byte[] Announce(byte kind, ulong node, ulong session, AbletonTimeline timeline, AbletonStartStop startStop, IPEndPoint measuredAt)
    {
        var writer = new Writer(stackalloc byte[Largest]);

        DiscoveryHeader(ref writer, kind, Ttl, node);

        writer.Entry(TimelineKey, 24);
        writer.Long(timeline.MicrosPerBeat);
        writer.Long(timeline.BeatOrigin);
        writer.Long(timeline.TimeOrigin);

        writer.Entry(SessionKey, 8);
        writer.Ulong(session);

        writer.Entry(StartStopKey, 17);
        writer.Byte(startStop.Playing ? (byte)1 : (byte)0);
        writer.Long(startStop.Beats);
        writer.Long(startStop.Timestamp);

        if (measuredAt.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> address = stackalloc byte[4];
            measuredAt.Address.TryWriteBytes(address, out _);

            writer.Entry(EndpointKey, 6);
            writer.Bytes(address);
            writer.Short((ushort)measuredAt.Port);
        }

        return writer.Done();
    }

    /// <summary>An app leaving.</summary>
    public static byte[] Goodbye(ulong node)
    {
        var writer = new Writer(stackalloc byte[Largest]);

        DiscoveryHeader(ref writer, ByeBye, 0, node);

        return writer.Done();
    }

    /// <summary>A ping carrying this side's clock, and the ghost time of the pong before where there was one.</summary>
    public static byte[] PingAt(long host, long previousGhost)
    {
        var writer = new Writer(stackalloc byte[Largest]);

        writer.Bytes(Measurement);
        writer.Byte(Ping);

        writer.Entry(HostKey, 8);
        writer.Long(host);

        if (previousGhost != 0)
        {
            writer.Entry(PreviousGhostKey, 8);
            writer.Long(previousGhost);
        }

        return writer.Done();
    }

    /// <summary>The answer to a ping: this side's session and ghost time, then the ping's own payload back.</summary>
    public static byte[] PongTo(ulong session, long ghost, ReadOnlySpan<byte> echoed)
    {
        var writer = new Writer(stackalloc byte[Largest]);

        writer.Bytes(Measurement);
        writer.Byte(Pong);

        writer.Entry(SessionKey, 8);
        writer.Ulong(session);

        writer.Entry(GhostKey, 8);
        writer.Long(ghost);

        writer.Bytes(echoed);

        return writer.Done();
    }

    /// <summary>Reads an announcement. False for anything else, a message of another group, or one malformed.</summary>
    public static bool TryRead(ReadOnlySpan<byte> packet, [NotNullWhen(true)] out AbletonMessage? message)
    {
        message = null;

        const int header = 8 + 1 + 1 + 2 + 8;

        if (packet.Length < header || packet.Length > Largest || !packet.StartsWith(Discovery)) return false;

        var kind = packet[8];
        var ttl = packet[9];
        var group = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        var node = BinaryPrimitives.ReadUInt64BigEndian(packet[12..]);

        if (group != 0 || kind is not (Alive or Response or ByeBye)) return false;

        ulong? session = null;
        AbletonTimeline? timeline = null;
        var startStop = default(AbletonStartStop);
        IPEndPoint? measuredAt = null;

        var rest = packet[header..];

        while (!rest.IsEmpty)
        {
            if (!NextEntry(ref rest, out var key, out var value)) return false;

            switch (key)
            {
                case TimelineKey when value.Length >= 24:
                    var perBeat = Long(value);

                    if (perBeat <= 0) return false;

                    timeline = new AbletonTimeline(perBeat, Long(value[8..]), Long(value[16..]));
                    break;
                case SessionKey when value.Length >= 8:
                    session = BinaryPrimitives.ReadUInt64BigEndian(value);
                    break;
                case StartStopKey when value.Length >= 17:
                    startStop = new AbletonStartStop(value[0] != 0, Long(value[1..]), Long(value[9..]));
                    break;
                case EndpointKey when value.Length >= 6:
                    var port = BinaryPrimitives.ReadUInt16BigEndian(value[4..]);

                    if (port != 0) measuredAt = new IPEndPoint(new IPAddress(value[..4]), port);
                    break;
                case TimelineKey or SessionKey or StartStopKey or EndpointKey:
                    return false;
            }
        }

        if (kind != ByeBye && (session is null || timeline is null)) return false;

        message = new AbletonMessage(kind, ttl, node, session, timeline, startStop, measuredAt);
        return true;
    }

    /// <summary>Reads a ping, handing back its payload to echo. False for anything else, or a payload too long to be a ping's.</summary>
    public static bool TryReadPing(ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> payload)
    {
        payload = default;

        if (packet.Length < 9 || !packet.StartsWith(Measurement) || packet[8] != Ping) return false;

        payload = packet[9..];

        return payload.Length <= LongestPing;
    }

    /// <summary>Reads a pong. False for anything else, or one malformed.</summary>
    public static bool TryReadPong(ReadOnlySpan<byte> packet, out AbletonPong pong)
    {
        pong = default;

        if (packet.Length < 9 || packet.Length > Largest || !packet.StartsWith(Measurement) || packet[8] != Pong) return false;

        ulong session = 0;
        long ghost = 0, previous = 0, host = 0;

        var rest = packet[9..];

        while (!rest.IsEmpty)
        {
            if (!NextEntry(ref rest, out var key, out var value)) return false;

            if (key is SessionKey or GhostKey or PreviousGhostKey or HostKey && value.Length < 8) return false;

            switch (key)
            {
                case SessionKey: session = BinaryPrimitives.ReadUInt64BigEndian(value); break;
                case GhostKey: ghost = Long(value); break;
                case PreviousGhostKey: previous = Long(value); break;
                case HostKey: host = Long(value); break;
            }
        }

        pong = new AbletonPong(session, ghost, previous, host);
        return true;
    }

    /// <summary>Whether <paramref name="packet"/> is one of the clock-measuring kind, ping or pong.</summary>
    public static bool Measures(ReadOnlySpan<byte> packet) => packet.StartsWith(Measurement);

    private static void DiscoveryHeader(ref Writer writer, byte kind, byte ttl, ulong node)
    {
        writer.Bytes(Discovery);
        writer.Byte(kind);
        writer.Byte(ttl);
        writer.Short(0);
        writer.Ulong(node);
    }

    /// <summary>Takes one entry off the front of <paramref name="rest"/>. False where its length runs past the end.</summary>
    private static bool NextEntry(ref ReadOnlySpan<byte> rest, out uint key, out ReadOnlySpan<byte> value)
    {
        key = 0;
        value = default;

        if (rest.Length < 8) return false;

        key = BinaryPrimitives.ReadUInt32BigEndian(rest);
        var size = BinaryPrimitives.ReadUInt32BigEndian(rest[4..]);

        if (size > (uint)(rest.Length - 8)) return false;

        value = rest.Slice(8, (int)size);
        rest = rest[(8 + (int)size)..];

        return true;
    }

    private static long Long(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt64BigEndian(bytes);

    /// <summary>Appends to a buffer on the stack, big-endian.</summary>
    private ref struct Writer(Span<byte> buffer)
    {
        private readonly Span<byte> buffer = buffer;

        private int at;

        public void Byte(byte value) => buffer[at++] = value;

        public void Short(ushort value) => Advance(2, BinaryPrimitives.TryWriteUInt16BigEndian(buffer[at..], value));

        public void Long(long value) => Advance(8, BinaryPrimitives.TryWriteInt64BigEndian(buffer[at..], value));

        public void Ulong(ulong value) => Advance(8, BinaryPrimitives.TryWriteUInt64BigEndian(buffer[at..], value));

        public void Entry(uint key, uint size)
        {
            Advance(4, BinaryPrimitives.TryWriteUInt32BigEndian(buffer[at..], key));
            Advance(4, BinaryPrimitives.TryWriteUInt32BigEndian(buffer[at..], size));
        }

        public void Bytes(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(buffer[at..]);
            at += bytes.Length;
        }

        public readonly byte[] Done() => buffer[..at].ToArray();

        private void Advance(int by, bool written)
        {
            if (!written) throw new InvalidOperationException("A Link message ran past its buffer.");

            at += by;
        }
    }
}
