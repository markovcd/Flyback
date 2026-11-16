using System.Net;
using Flyback.Host.Ableton;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Ableton;

/// <summary>
/// Apps on one network end up in one Link session, keep one tempo and one beat,
/// and take the newest change any of them makes. Played between sessions in
/// memory, each on a clock of its own, over a network that delivers at once.
/// </summary>
public class AbletonSessionTests
{
    private const long Second = 1_000_000;

    [Fact]
    public void An_app_joins_the_older_session_and_keeps_its_beat()
    {
        var network = new Network();
        var older = network.Found(node: 20, skew: 0);

        network.Run(10 * Second);

        var younger = network.Found(node: 10, skew: 123_456_789);

        network.Run(2 * Second);

        younger.Link.Session.ShouldBe(older.Link.Node);
        older.Link.Session.ShouldBe(older.Link.Node);
        younger.Link.Peers.ShouldBe(1);
        older.Link.Peers.ShouldBe(1);

        younger.Link.BeatAt(younger.Host).ShouldBe(older.Link.BeatAt(older.Host), 1e-6);
    }

    [Fact]
    public void Of_two_sessions_about_as_old_the_smaller_id_wins()
    {
        var network = new Network();
        var first = network.Found(node: 20, skew: 5 * Second);

        network.Run(Second / 10);

        var second = network.Found(node: 10, skew: -7 * Second);

        network.Run(2 * Second);

        first.Link.Session.ShouldBe(10UL);
        second.Link.Session.ShouldBe(10UL);
        first.Link.BeatAt(first.Host).ShouldBe(second.Link.BeatAt(second.Host), 1e-6);
    }

    [Fact]
    public void A_tempo_changed_on_a_later_beat_is_taken()
    {
        var app = Alone(out var now);
        var faster = new AbletonTimeline(428_571, 4_000_000, 2 * Second);

        app.Hear(Announcement(node: 99, session: app.Node, faster), Somewhere, now);

        app.Timeline.ShouldBe(faster);
        app.TakeChanged().ShouldBeTrue();
    }

    [Fact]
    public void A_tempo_from_an_earlier_beat_is_not_taken()
    {
        var app = Alone(out var now);
        app.Hear(Announcement(99, app.Node, new AbletonTimeline(428_571, 4_000_000, 2 * Second)), Somewhere, now);

        app.Hear(Announcement(98, app.Node, new AbletonTimeline(300_000, 1_000_000, Second)), Somewhere, now);

        app.Timeline.MicrosPerBeat.ShouldBe(428_571);
    }

    [Fact]
    public void A_tempo_past_what_a_session_plays_is_held_to_it()
    {
        var app = Alone(out var now);

        app.Hear(Announcement(99, app.Node, new AbletonTimeline(1_000, 4_000_000, 0)), Somewhere, now);

        app.Timeline.Bpm.ShouldBe(999d, 0.01);
    }

    [Fact]
    public void The_newest_start_or_stop_is_taken()
    {
        var app = Alone(out var now);

        app.Hear(Announcement(99, app.Node, AbletonTimeline.Founding, new AbletonStartStop(true, 0, 3 * Second)), Somewhere, now);
        app.Hear(Announcement(98, app.Node, AbletonTimeline.Founding, new AbletonStartStop(false, 0, 2 * Second)), Somewhere, now);

        app.StartStop.Playing.ShouldBeTrue();
    }

    [Fact]
    public void An_app_that_goes_quiet_is_forgotten_and_one_that_says_goodbye_at_once()
    {
        var app = Alone(out var now);

        app.Hear(Announcement(99, app.Node, AbletonTimeline.Founding), Somewhere, now);
        app.Hear(Announcement(98, app.Node, AbletonTimeline.Founding), Somewhere, now);
        app.Peers.ShouldBe(2);

        app.Hear(AbletonWire.Goodbye(98), Somewhere, now);
        app.Peers.ShouldBe(1);

        app.Advance(now + AbletonWire.Ttl * Second + 1);
        app.Peers.ShouldBe(0);
    }

    [Fact]
    public void Its_own_announcements_are_not_an_app()
    {
        var app = Alone(out var now);

        app.Hear(app.Announce(AbletonWire.Alive, Somewhere), Somewhere, now);

        app.Peers.ShouldBe(0);
        app.Drain().ShouldBeEmpty();
    }

    [Fact]
    public void An_announcement_is_answered_and_a_ping_ponged()
    {
        var app = Alone(out var now);

        app.Hear(Announcement(99, 5, AbletonTimeline.Founding, measuredAt: null), Somewhere, now);
        app.Hear(AbletonWire.PingAt(1234, 0), Somewhere, now);

        var sent = app.Drain();

        AbletonWire.TryRead(sent[0].Bytes, out var answer).ShouldBeTrue();
        answer.Kind.ShouldBe(AbletonWire.Response);
        sent[0].To.ShouldBe(Somewhere);

        AbletonWire.TryReadPong(sent[^1].Bytes, out var pong).ShouldBeTrue();
        pong.Session.ShouldBe(app.Session);
        pong.Ghost.ShouldBe(now + app.Offset);
        pong.Host.ShouldBe(1234);
    }

    private static readonly IPEndPoint Somewhere = new(IPAddress.Parse("10.0.0.9"), 40000);

    private static readonly IPEndPoint Here = new(IPAddress.Parse("10.0.0.1"), 40001);

    private static AbletonSession Alone(out long now)
    {
        now = 50 * Second;
        return new AbletonSession(1, 50 * Second - 3 * Second, _ => Here);
    }

    private static byte[] Announcement(ulong node, ulong session, AbletonTimeline timeline, AbletonStartStop startStop = default, IPEndPoint? measuredAt = null) =>
        AbletonWire.Announce(AbletonWire.Alive, node, session, timeline, startStop, measuredAt ?? Somewhere);

    /// <summary>Apps on one network, each on a clock skewed from the network's by its own amount.</summary>
    private sealed class Network
    {
        private const long Step = 1_000;

        private const long Announcing = 250_000;

        private readonly List<App> apps = [];

        private long now = 1_000 * Second;

        public App Found(ulong node, long skew)
        {
            var at = new IPEndPoint(IPAddress.Parse($"10.0.0.{apps.Count + 1}"), 20000);
            var app = new App(this, at, skew);
            app.Link = new AbletonSession(node, app.Host, _ => at);
            apps.Add(app);
            return app;
        }

        public long Now => now;

        public void Run(long length)
        {
            for (var end = now + length; now < end; now += Step)
            {
                foreach (var app in apps) app.Link.Advance(app.Host);

                if (now % Announcing == 0)
                    foreach (var sender in apps)
                        foreach (var other in apps.Where(other => other != sender))
                            other.Link.Hear(sender.Link.Announce(AbletonWire.Alive, sender.At), sender.At, other.Host);

                Deliver();
            }
        }

        private void Deliver()
        {
            for (var moved = true; moved;)
            {
                moved = false;

                foreach (var sender in apps)
                    foreach (var packet in sender.Link.Drain())
                    {
                        var to = apps.Single(app => app.At.Equals(packet.To));
                        to.Link.Hear(packet.Bytes, sender.At, to.Host);
                        moved = true;
                    }
            }
        }
    }

    private sealed class App(Network network, IPEndPoint at, long skew)
    {
        public IPEndPoint At { get; } = at;

        public AbletonSession Link { get; set; } = null!;

        public long Host => network.Now + skew;
    }
}
