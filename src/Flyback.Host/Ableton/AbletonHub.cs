using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Host.Ableton;

/// <summary>
/// Flyback on the network as one more app in an Ableton Link session, joined
/// only while a running program follows it, and written into that program's
/// block as a clock.
/// </summary>
/// <remarks>
/// One thread owns the sockets and the <see cref="AbletonSession"/>: it waits a
/// couple of milliseconds for packets, answers them, announces this side four
/// times a second or as soon as the session changes, and writes the clock
/// whenever the beat has moved a tick. IPv4 only, on every network the machine
/// is on, with one socket each to announce and answer from and one shared
/// socket listening on the group.
/// <para>
/// Not on the page, which has no UDP, nor on iOS, where multicast needs an
/// entitlement Apple grants by request.
/// </para>
/// </remarks>
internal sealed class AbletonHub : IDisposable
{
    /// <summary>How long the thread waits for a packet, in microseconds, which bounds how late a tick is written.</summary>
    private const int Waiting = 2_000;

    private const long Announcing = 250_000;

    /// <summary>The least time between two announcements, so a burst of changes is not a flood.</summary>
    private const long Soonest = 50_000;

    private readonly Lock gate = new();

    private readonly AbletonClock clock = new();

    private LiveValues[] following = [];

    private AbletonView view = AbletonView.Apart;

    private Thread? worker;

    private volatile bool stopping;

    public static bool Possible =>
        !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi() && !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS();

    /// <summary>Said once where the network will not have this side, with why.</summary>
    public event Action<string>? Trouble;

    /// <summary>Microseconds on a steady clock.</summary>
    internal Func<long> Now { get; set; } = () => (long)(Stopwatch.GetTimestamp() * (1e6 / Stopwatch.Frequency));

    /// <summary>
    /// Points this at the programs now running: joins the session if any of them
    /// follows it and leaves if none does, then writes the clock into each.
    /// </summary>
    public void Follow(params LiveValues[] blocks)
    {
        lock (gate) following = blocks;

        if (blocks.Any(Reads))
            Join();
        else
            Leave();

        Publish();
    }

    /// <summary>Writes the clock into a new program's block before it is heard.</summary>
    public void Fill(LiveValues block)
    {
        lock (gate) clock.WriteTo(block);
    }

    public AbletonView Look()
    {
        lock (gate) return view;
    }

    /// <summary>Joins the network, or does nothing where already on it or where it cannot be.</summary>
    public void Join()
    {
        if (worker is not null || !Possible) return;

        IDisposable permit;
        Socket listener;
        List<AbletonRoute> routes;

        try
        {
            (listener, routes) = Open();
            permit = MulticastPermit.Take();
        }
        catch (Exception ex) when (ex is SocketException or NetworkInformationException or InvalidOperationException or PlatformNotSupportedException)
        {
            Trouble?.Invoke($"Could not join Ableton Link: {ex.Message}");
            return;
        }

        stopping = false;
        worker = new Thread(() => Run(listener, routes, permit)) { IsBackground = true, Name = "Ableton Link" };
        worker.Start();
    }

    /// <summary>Says goodbye and leaves the network. The beat holds where it was.</summary>
    public void Leave()
    {
        if (worker is null) return;

        stopping = true;
        worker.Join();
        worker = null;

        lock (gate)
        {
            clock.Stop();
            view = AbletonView.Apart;
        }

        Publish();
    }

    public void Dispose() => Leave();

    private static bool Reads(LiveValues block) =>
        block.Keys.Any(key => MidiSignal.SourceOf(key) == MidiSources.Ableton);

    private void Publish()
    {
        lock (gate)
            foreach (var block in following)
                clock.WriteTo(block);
    }

    private void Run(Socket listener, List<AbletonRoute> routes, IDisposable permit)
    {
        var buffer = new byte[2048];
        var link = new AbletonSession(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)), Now(), remote => Facing(routes, remote).Local);
        Socket[] sockets = [listener, .. routes.Select(route => route.Socket)];
        var readable = new List<Socket>(sockets.Length);
        var nextAnnouncement = 0L;
        var lastAnnouncement = long.MinValue / 2;
        var pending = false;

        while (!stopping)
        {
            readable.Clear();
            readable.AddRange(sockets);

            try
            {
                Socket.Select(readable, null, null, Waiting);
            }
            catch (SocketException)
            {
                readable.Clear();
                Thread.Sleep(Waiting / 1000);
            }

            foreach (var socket in readable) Receive(socket, buffer, link);

            var now = Now();

            link.Advance(now);
            pending |= link.TakeChanged();

            if ((pending && now - lastAnnouncement >= Soonest) || now >= nextAnnouncement)
            {
                foreach (var route in routes) Send(route.Socket, link.Announce(AbletonWire.Alive, route.Local), AbletonWire.Group);

                lastAnnouncement = now;
                nextAnnouncement = now + Announcing;
                pending = false;
            }

            foreach (var packet in link.Drain()) Send(Facing(routes, packet.To.Address).Socket, packet.Bytes, packet.To);

            lock (gate)
            {
                if (clock.Read(link, now))
                    foreach (var block in following)
                        clock.WriteTo(block);

                view = new AbletonView(link.Session.ToString("x16"), link.Peers, link.Timeline.Bpm, clock.Beat, clock.Running);
            }
        }

        foreach (var route in routes) Send(route.Socket, link.Goodbye(), AbletonWire.Group);

        listener.Dispose();
        foreach (var route in routes) route.Socket.Dispose();
        permit.Dispose();
    }

    private void Receive(Socket socket, byte[] buffer, AbletonSession link)
    {
        while (socket.Available > 0)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int length;

            try
            {
                length = socket.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException)
            {
                // A packet too long for the buffer, or an unreachable port reported
                // back from an earlier send: neither is anything to answer.
                continue;
            }

            link.Hear(buffer.AsSpan(0, length), (IPEndPoint)from, Now());
        }
    }

    private static void Send(Socket socket, byte[] bytes, IPEndPoint to)
    {
        try
        {
            socket.SendTo(bytes, to);
        }
        catch (SocketException)
        {
            // A network that went away: the next announcement tries again.
        }
    }

    /// <summary>The route on the same network as <paramref name="remote"/>, or the first.</summary>
    private static AbletonRoute Facing(List<AbletonRoute> routes, IPAddress remote) =>
        routes.FirstOrDefault(route => route.Faces(remote)) ?? routes[0];

    /// <summary>The listener on the group, and a route for every network with an IPv4 address; the loopback alone where there is none.</summary>
    private static (Socket Listener, List<AbletonRoute> Routes) Open()
    {
        var networks = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.SupportsMulticast && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => (address.Address, Mask: address.IPv4Mask))
            .ToList();

        if (networks.Count == 0) networks.Add((IPAddress.Loopback, new IPAddress([255, 0, 0, 0])));

        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var routes = new List<AbletonRoute>();

        try
        {
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Bind(new IPEndPoint(IPAddress.Any, AbletonWire.Group.Port));

            foreach (var (address, mask) in networks)
            {
                try
                {
                    listener.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(AbletonWire.Group.Address, address));
                }
                catch (SocketException)
                {
                    // A network that will not take the group still announces from its socket.
                }

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(address, 0));
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.MulticastLoopback = true;

                routes.Add(new AbletonRoute(socket, (IPEndPoint)socket.LocalEndPoint!, mask));
            }

            return (listener, routes);
        }
        catch
        {
            listener.Dispose();
            foreach (var route in routes) route.Socket.Dispose();
            throw;
        }
    }
}
