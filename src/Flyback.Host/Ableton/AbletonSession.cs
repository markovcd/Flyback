using System.Net;

namespace Flyback.Host.Ableton;

/// <summary>
/// Flyback's place in Ableton Link: which session it is in, that session's tempo
/// and beat, and what to say to the other apps on the network. No sockets and no
/// clock of its own: packets and the time are handed in, and what to send is
/// taken out, so two of these can play a whole session against each other in a
/// test.
/// </summary>
/// <remarks>
/// Times are microseconds on this side's steady clock. A session shares a ghost
/// time, which is this side's clock plus <see cref="Offset"/>; an app founds a
/// session with ghost time nought at the moment it started, and one that joins
/// measures its offset by pinging an app already in it.
/// <para>
/// Two rules settle everything. Of two sessions, the one whose ghost time is
/// further on by more than half a second is older and wins, and between two
/// about as old the smaller id wins, so every app ends up in one. Within a
/// session, the timeline with the latest beat origin wins, since whoever
/// changes the tempo moves the origin to the beat they changed it on, and the
/// transport with the latest timestamp wins.
/// </para>
/// </remarks>
internal sealed class AbletonSession
{
    /// <summary>How far one session's ghost time must be ahead of another's to win outright.</summary>
    private const long Older = 500_000;

    /// <summary>How often the session this side is in is measured again.</summary>
    private const long Remeasuring = 30_000_000;

    /// <summary>How long a session whose measuring failed is left before trying again.</summary>
    private const long Resting = 1_000_000;

    private readonly Func<IPAddress, IPEndPoint?> facing;

    private readonly Dictionary<ulong, AbletonPeer> peers = [];

    /// <summary>The timelines of sessions other than this side's, while any app in them is heard.</summary>
    private readonly Dictionary<ulong, AbletonTimeline> others = [];

    private readonly Dictionary<ulong, long> resting = [];

    private readonly List<AbletonMeasurement> measuring = [];

    private readonly List<AbletonPacket> outbox = [];

    private long remeasureAt;

    private bool changed = true;

    /// <param name="node">This app's id on the network.</param>
    /// <param name="now">This side's clock as it founds its own session.</param>
    /// <param name="facing">Where this side answers pings, on whichever network faces an address, or null where none does.</param>
    public AbletonSession(ulong node, long now, Func<IPAddress, IPEndPoint?> facing)
    {
        this.facing = facing;

        Node = node;
        Session = node;
        Offset = -now;
        remeasureAt = now + Remeasuring;
    }

    public ulong Node { get; }

    /// <summary>The session this side is in: its own until it joins another.</summary>
    public ulong Session { get; private set; }

    /// <summary>The session's ghost time less this side's clock.</summary>
    public long Offset { get; private set; }

    public AbletonTimeline Timeline { get; private set; } = AbletonTimeline.Founding;

    public AbletonStartStop StartStop { get; private set; }

    /// <summary>How many other apps are in the session with this one.</summary>
    public int Peers => peers.Values.Count(peer => peer.Session == Session);

    /// <summary>The session's beat at <paramref name="now"/>.</summary>
    public double BeatAt(long now) => Timeline.BeatAt(now + Offset);

    /// <summary>A packet arrived from <paramref name="from"/>.</summary>
    public void Hear(ReadOnlySpan<byte> packet, IPEndPoint from, long now)
    {
        if (AbletonWire.Measures(packet))
        {
            Measure(packet, from, now);
            return;
        }

        if (!AbletonWire.TryRead(packet, out var message) || message.Node == Node) return;

        if (message.Kind == AbletonWire.ByeBye)
        {
            peers.Remove(message.Node);
            return;
        }

        if (message.Kind == AbletonWire.Alive && facing(from.Address) is { } mine)
            outbox.Add(new AbletonPacket(Announce(AbletonWire.Response, mine), from));

        var peer = new AbletonPeer(
            message.Node,
            message.Session!.Value,
            message.Timeline!.Value,
            message.StartStop,
            message.MeasuredAt,
            now + message.Ttl * 1_000_000L);

        peers[peer.Node] = peer;

        Saw(peer, now);
    }

    /// <summary>Forgets apps that went quiet, sends pings again that went unanswered, and measures this session again when it is due.</summary>
    public void Advance(long now)
    {
        foreach (var gone in peers.Values.Where(peer => peer.Expires < now).ToList())
            peers.Remove(gone.Node);

        foreach (var empty in others.Keys.Where(id => !peers.Values.Any(peer => peer.Session == id) && !measuring.Any(m => m.Session == id)).ToList())
            others.Remove(empty);

        foreach (var run in measuring.ToList())
        {
            if (run.Overdue(now) is { } again) outbox.Add(new AbletonPacket(again, run.Target));

            Settle(run, now);
        }

        if (now < remeasureAt) return;

        remeasureAt = now + Remeasuring;

        if (Peers > 0) Launch(Session, now);
    }

    /// <summary>This side's state, as an announcement or the response to one, saying it answers pings at <paramref name="measuredAt"/>.</summary>
    public byte[] Announce(byte kind, IPEndPoint measuredAt) =>
        AbletonWire.Announce(kind, Node, Session, Timeline, StartStop, measuredAt);

    public byte[] Goodbye() => AbletonWire.Goodbye(Node);

    /// <summary>Whether the session has changed since this was last asked, so the other apps should hear now rather than at the next announcement.</summary>
    public bool TakeChanged()
    {
        var was = changed;
        changed = false;
        return was;
    }

    /// <summary>Everything waiting to be sent, taken.</summary>
    public IReadOnlyList<AbletonPacket> Drain()
    {
        var taken = outbox.ToArray();
        outbox.Clear();
        return taken;
    }

    private void Saw(AbletonPeer peer, long now)
    {
        if (peer.Session == Session)
        {
            Adopt(peer);
            return;
        }

        if (others.TryGetValue(peer.Session, out var known))
        {
            if (peer.Timeline.BeatOrigin > known.BeatOrigin) others[peer.Session] = peer.Timeline;
            return;
        }

        if (resting.TryGetValue(peer.Session, out var until) && now < until) return;

        resting.Remove(peer.Session);
        others[peer.Session] = peer.Timeline;
        Launch(peer.Session, now);
    }

    /// <summary>Takes a peer's timeline and transport where they are newer than this side's. The peer is in this side's session.</summary>
    private void Adopt(AbletonPeer peer)
    {
        if (peer.Timeline.BeatOrigin > Timeline.BeatOrigin)
        {
            Timeline = peer.Timeline.Clamped();
            changed = true;
        }

        if (peer.StartStop.Timestamp > StartStop.Timestamp)
        {
            StartStop = peer.StartStop;
            changed = true;
        }
    }

    /// <summary>Starts pinging an app in <paramref name="session"/>, its founder where it is still here.</summary>
    private void Launch(ulong session, long now)
    {
        if (measuring.Any(run => run.Session == session)) return;

        var target = peers.Values
            .Where(peer => peer.Session == session && peer.MeasuredAt is not null)
            .OrderBy(peer => peer.Node == session ? 0 : 1)
            .FirstOrDefault();

        if (target is null)
        {
            Failed(session, now);
            return;
        }

        var run = new AbletonMeasurement(session, target.MeasuredAt!);
        measuring.Add(run);
        outbox.Add(new AbletonPacket(run.Begin(now), run.Target));
    }

    private void Measure(ReadOnlySpan<byte> packet, IPEndPoint from, long now)
    {
        if (AbletonWire.TryReadPing(packet, out var payload))
        {
            outbox.Add(new AbletonPacket(AbletonWire.PongTo(Session, now + Offset, payload), from));
            return;
        }

        if (!AbletonWire.TryReadPong(packet, out var pong)) return;

        var run = measuring.Find(m => m.Target.Equals(from));

        if (run is null) return;

        if (run.Answered(pong, now) is { } next) outbox.Add(new AbletonPacket(next, run.Target));

        Settle(run, now);
    }

    private void Settle(AbletonMeasurement run, long now)
    {
        if (!run.Finished) return;

        measuring.Remove(run);

        if (run.Offset is { } offset)
            Measured(run.Session, offset, now);
        else
            Failed(run.Session, now);
    }

    private void Measured(ulong session, long offset, long now)
    {
        if (session == Session)
        {
            Offset = offset;
            return;
        }

        if (!others.TryGetValue(session, out var timeline)) return;

        var ahead = offset - Offset;

        if (ahead <= Older && (Math.Abs(ahead) >= Older || session >= Session)) return;

        others.Remove(session);
        others[Session] = Timeline;

        Session = session;
        Offset = offset;
        Timeline = timeline.Clamped();
        StartStop = default;
        changed = true;
        remeasureAt = now + Remeasuring;

        foreach (var peer in peers.Values.Where(peer => peer.Session == session).ToList()) Adopt(peer);
    }

    private void Failed(ulong session, long now)
    {
        if (session == Session) return;

        others.Remove(session);
        resting[session] = now + Resting;
    }
}
