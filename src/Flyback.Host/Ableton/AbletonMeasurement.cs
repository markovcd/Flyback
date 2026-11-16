using System.Net;

namespace Flyback.Host.Ableton;

/// <summary>
/// One run of pings to an app in another session, finding how far its ghost time
/// is from this side's clock.
/// </summary>
/// <remarks>
/// Each pong gives two estimates of the offset: its ghost time against the middle
/// of the round trip it answered, and the middle of it and the pong before
/// against the host time between them. The median of a hundred is the answer,
/// which a few late packets cannot move. A ping unanswered after
/// <see cref="Patience"/> is sent again, and after <see cref="Retries"/> of
/// those the run has failed.
/// </remarks>
internal sealed class AbletonMeasurement(ulong session, IPEndPoint target)
{
    private const int Wanted = 100;

    private const int Retries = 5;

    /// <summary>Microseconds to wait for a pong.</summary>
    private const long Patience = 50_000;

    private readonly List<double> samples = [];

    private int retried;

    private long due;

    /// <summary>The session being measured.</summary>
    public ulong Session { get; } = session;

    /// <summary>Where its app answers pings.</summary>
    public IPEndPoint Target { get; } = target;

    /// <summary>Ghost time less this side's clock, in microseconds, once measured.</summary>
    public long? Offset { get; private set; }

    public bool Failed { get; private set; }

    public bool Finished => Failed || Offset is not null;

    /// <summary>The first ping.</summary>
    public byte[] Begin(long now)
    {
        due = now + Patience;
        return AbletonWire.PingAt(now, 0);
    }

    /// <summary>A ping sent again where the last went unanswered, or null where it is not yet late or the run is over.</summary>
    public byte[]? Overdue(long now)
    {
        if (Finished || now < due) return null;

        if (++retried >= Retries)
        {
            Failed = true;
            return null;
        }

        due = now + Patience;
        return AbletonWire.PingAt(now, 0);
    }

    /// <summary>A pong arrived: the next ping, or null once the run is over.</summary>
    public byte[]? Answered(AbletonPong pong, long now)
    {
        if (Finished) return null;

        if (pong.Session != Session)
        {
            Failed = true;
            return null;
        }

        if (pong.Ghost != 0 && pong.Host != 0)
        {
            samples.Add(pong.Ghost - (now + pong.Host) * 0.5);

            if (pong.PreviousGhost != 0) samples.Add((pong.Ghost + pong.PreviousGhost) * 0.5 - pong.Host);
        }

        if (samples.Count > Wanted)
        {
            Offset = (long)Math.Round(Median(samples));
            return null;
        }

        due = now + Patience;
        return AbletonWire.PingAt(now, pong.Ghost);
    }

    private static double Median(List<double> values)
    {
        values.Sort();

        var middle = values.Count / 2;

        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) * 0.5;
    }
}
