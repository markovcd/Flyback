using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Host.Ableton;

/// <summary>
/// A Link session read as the five numbers a Clock In follows: the beat, the
/// rate it moves at, the tempo, whether it is running, and a count of Starts.
/// </summary>
/// <remarks>
/// A session's beats can be any size, so the beat is counted from a bar line
/// found when this side joins: a multiple of four beats back, which keeps a bar
/// of four in phase with every other app. A session whose apps never press
/// Start or Stop is running throughout; one whose apps do runs as they say, and
/// holds its beat while stopped, as a MIDI clock's does.
/// </remarks>
internal sealed class AbletonClock
{
    /// <summary>The beats a bar, which the beat is counted from a multiple of.</summary>
    private const double Bar = 4d;

    /// <summary>
    /// How far the beat moves before it is worth writing again: three quarters of
    /// a MIDI clock's tick. More than the half tick a Clock In takes for one, and
    /// less than a whole, so readings that land a tick apart are each written.
    /// </summary>
    private const double Step = 0.75d / MidiSignal.TicksPerBeat;

    private ulong? session;

    private double bar;

    private double written = double.NaN;

    private bool dirty = true;

    public float Beat { get; private set; }

    public float Rate => Running ? Bpm / 60f : 0f;

    public float Bpm { get; private set; }

    public bool Running { get; private set; }

    public float Starts { get; private set; }

    /// <summary>Reads the session at <paramref name="now"/>. True where the numbers have moved enough to be written.</summary>
    public bool Read(AbletonSession link, long now)
    {
        var beat = link.BeatAt(now);

        if (session != link.Session)
        {
            session = link.Session;
            bar = Math.Floor(beat / Bar) * Bar;
            dirty = true;
        }

        var running = link.StartStop.Timestamp == 0 || link.StartStop.Playing;
        var bpm = (float)link.Timeline.Bpm;

        if (running && !Running) Starts++;

        dirty |= running != Running || bpm != Bpm;

        Running = running;
        Bpm = bpm;

        if (Running && !(Math.Abs(beat - written) < Step))
        {
            Beat = (float)(beat - bar);
            written = beat;
            dirty = true;
        }

        var moved = dirty;
        dirty = false;
        return moved;
    }

    /// <summary>The session is left: the beat holds where it was.</summary>
    public void Stop()
    {
        Running = false;
        session = null;
        written = double.NaN;
    }

    /// <summary>Puts these into a program's live-input block, under the Link session's name.</summary>
    public void WriteTo(LiveValues block)
    {
        block.Set(MidiSignal.ClockKey(MidiSources.Ableton, MidiSignal.Beat), Beat);
        block.Set(MidiSignal.ClockKey(MidiSources.Ableton, MidiSignal.Rate), Rate);
        block.Set(MidiSignal.ClockKey(MidiSources.Ableton, MidiSignal.Bpm), Bpm);
        block.Set(MidiSignal.ClockKey(MidiSources.Ableton, MidiSignal.Running), Running ? 1f : 0f);
        block.Set(MidiSignal.ClockKey(MidiSources.Ableton, MidiSignal.Starts), Starts);
    }
}
