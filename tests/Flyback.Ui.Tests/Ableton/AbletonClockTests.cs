using System.Net;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Host.Ableton;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Ableton;

/// <summary>A Link session read as a Clock In reads a MIDI clock: a beat in phase with the session's bars, its tempo, and its transport.</summary>
public class AbletonClockTests
{
    private const long Second = 1_000_000;

    private static readonly IPEndPoint Somewhere = new(IPAddress.Parse("10.0.0.9"), 40000);

    [Fact]
    public void The_beat_is_counted_from_the_bar_line_before_it()
    {
        var (link, clock) = Joined(sessionBeat: 13.5);

        clock.Beat.ShouldBe(1.5f, 1e-4f);
    }

    [Fact]
    public void The_tempo_and_the_rate_are_the_sessions()
    {
        var (_, clock) = Joined(sessionBeat: 0, microsPerBeat: 428_571);

        clock.Bpm.ShouldBe(140f, 0.01f);
        clock.Rate.ShouldBe(140f / 60f, 1e-3f);
    }

    [Fact]
    public void A_session_nobody_starts_or_stops_is_running_from_the_moment_it_is_joined()
    {
        var (_, clock) = Joined(sessionBeat: 0);

        clock.Running.ShouldBeTrue();
        clock.Starts.ShouldBe(1f);
    }

    [Fact]
    public void Stopped_the_beat_holds_and_started_again_it_is_counted()
    {
        var link = new AbletonSession(1, 0, _ => Somewhere);
        var clock = new AbletonClock();

        clock.Read(link, 0);
        link.Hear(Told(link, AbletonTimeline.Founding, new AbletonStartStop(false, 0, Second)), Somewhere, Second);
        clock.Read(link, Second).ShouldBeTrue();
        var held = clock.Beat;

        clock.Running.ShouldBeFalse();
        clock.Rate.ShouldBe(0f);
        clock.Read(link, 3 * Second);
        clock.Beat.ShouldBe(held);

        link.Hear(Told(link, AbletonTimeline.Founding, new AbletonStartStop(true, 0, 4 * Second)), Somewhere, 4 * Second);
        clock.Read(link, 4 * Second);

        clock.Running.ShouldBeTrue();
        clock.Starts.ShouldBe(2f);
        clock.Beat.ShouldBe(8f, 1e-4f);
    }

    [Fact]
    public void It_is_written_only_once_the_beat_has_moved_most_of_a_tick()
    {
        var (link, clock) = Joined(sessionBeat: 0);
        var tick = 500_000 / MidiSignal.TicksPerBeat;

        clock.Read(link, tick / 2).ShouldBeFalse();
        clock.Read(link, tick + 1).ShouldBeTrue();
    }

    [Fact]
    public void A_program_reading_the_session_finds_all_five_numbers()
    {
        var (_, clock) = Joined(sessionBeat: 6, microsPerBeat: 500_000);
        string[] keys = [.. new[] { MidiSignal.Beat, MidiSignal.Rate, MidiSignal.Bpm, MidiSignal.Running, MidiSignal.Starts }
            .Select(signal => MidiSignal.ClockKey(MidiSources.Ableton, signal))];
        var block = new LiveValues(keys);

        clock.WriteTo(block);

        block.Find(keys[0]).ShouldBe(2f);
        block.Find(keys[1]).ShouldBe(2f);
        block.Find(keys[2]).ShouldBe(120f);
        block.Find(keys[3]).ShouldBe(1f);
        block.Find(keys[4]).ShouldBe(1f);
    }

    /// <summary>
    /// A session founded at nought, told by another app of a timeline that puts
    /// <paramref name="sessionBeat"/> at nought, and read once there. The timeline
    /// is anchored four beats on, so its origin is later than the founding one's
    /// and it is taken.
    /// </summary>
    private static (AbletonSession Link, AbletonClock Clock) Joined(double sessionBeat, long microsPerBeat = 500_000)
    {
        var link = new AbletonSession(1, 0, _ => Somewhere);
        var clock = new AbletonClock();

        link.Hear(Told(link, new AbletonTimeline(microsPerBeat, (long)((sessionBeat + 4) * 1e6), 4 * microsPerBeat)), Somewhere, 0);
        clock.Read(link, 0);

        return (link, clock);
    }

    /// <summary>Another app in <paramref name="link"/>'s session announcing <paramref name="timeline"/> and <paramref name="startStop"/>.</summary>
    private static byte[] Told(AbletonSession link, AbletonTimeline timeline, AbletonStartStop startStop = default) =>
        AbletonWire.Announce(AbletonWire.Alive, 99, link.Session, timeline, startStop, Somewhere);
}
