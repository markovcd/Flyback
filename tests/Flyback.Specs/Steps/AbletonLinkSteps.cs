using System.Net;
using Flyback.Core.Graph;
using Flyback.Host.Ableton;
using Flyback.Specs.Support;
using Reqnroll;

namespace Flyback.Specs.Steps;

/// <summary>
/// Another app in an Ableton Link session sets its tempo and presses Start and
/// Stop, and Flyback's side of the session is read into the patch as the shell
/// reads it, with the sound played on between as it would be in the room.
/// </summary>
/// <remarks>
/// The session is Flyback's own, kept in memory with no sockets: the other app
/// is the announcements it would send. This side's clock is the sound's, in
/// microseconds.
/// </remarks>
[Binding]
public sealed class AbletonLinkSteps(PatchContext context)
{
    private const ulong Other = 99;

    private static readonly IPEndPoint There = new(IPAddress.Parse("10.0.0.9"), 40000);

    private readonly AbletonSession link = new(1, 0, _ => There);

    /// <summary>The bar the other app's latest change is anchored on, which grows with each so the session takes it.</summary>
    private long bar;

    [When(@"^the Ableton Link session plays (\d+) beats? at (\d+) bpm$")]
    public void WhenTheSessionPlays(int beats, int bpm) => WhenTheSessionPlaysInto(beats, bpm, 0);

    [When(@"^the Ableton Link session plays (\d+) beats? at (\d+) bpm, starting (\d+) beats? into a bar$")]
    public void WhenTheSessionPlaysInto(int beats, int bpm, int into)
    {
        var perBeat = (long)Math.Round(60e6 / bpm);
        var from = context.Now;

        bar += 4;
        Tell(new AbletonTimeline(perBeat, (bar + into) * 1_000_000, Micros(from)), link.StartStop);

        var tick = 60d / bpm / MidiSignal.TicksPerBeat;

        for (var k = 0; k <= beats * MidiSignal.TicksPerBeat; k++)
        {
            var at = from + k * tick;

            context.PlayUntil(at + context.Lead);
            context.Ableton.Read(link, Micros(at));
            context.Push();
        }
    }

    [When("an app in the Ableton Link session presses Stop")]
    public void WhenStopped()
    {
        var now = Micros(context.Now);

        Tell(link.Timeline, new AbletonStartStop(false, (long)(link.BeatAt(now) * 1e6), now + link.Offset));
        context.Ableton.Read(link, now);
        context.Push();
    }

    private void Tell(AbletonTimeline timeline, AbletonStartStop startStop) =>
        link.Hear(AbletonWire.Announce(AbletonWire.Alive, Other, link.Session, timeline, startStop, There), There, Micros(context.Now));

    private static long Micros(double seconds) => (long)Math.Round(seconds * 1e6);
}
