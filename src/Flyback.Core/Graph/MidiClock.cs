using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// One instrument's clock, followed: where its song is in beats, whether it is
/// running, and the tempo it is sending.
/// </summary>
/// <remarks>
/// What the wire carries is twenty-four ticks a beat and four transport
/// messages, and this turns them into the numbers a Clock In reads. The beat is
/// a count of ticks, so it moves in steps; the program smooths between them
/// against its own clock, which is the one clock every evaluation already has
/// (see <c>NodeCatalog.EmitClock</c>). The tempo is measured from the spacing
/// of the ticks on whatever clock the caller keeps, and only its rate matters,
/// so the shell's stopwatch will do.
/// <para>
/// Written on the thread the ticks arrive on and read on the thread that plays,
/// through <see cref="LiveValues"/>, which is what that block is for.
/// </para>
/// </remarks>
public sealed class MidiClock
{
    /// <summary>What MIDI settled on in 1983.</summary>
    public const int TicksPerBeat = 24;

    /// <summary>A song position is sent in sixteenth notes.</summary>
    private const double SixteenthsPerBeat = 4d;

    /// <summary>
    /// The weight of the newest gap between ticks in the measured tempo: about a
    /// beat's worth of ticks settle a change, and the jitter of any one is
    /// divided by as many.
    /// </summary>
    private const double Smoothing = 1d / TicksPerBeat;

    /// <summary>
    /// A gap longer than this is the clock having stopped, not a slow tempo:
    /// twenty beats a minute is an eighth of a second between ticks.
    /// </summary>
    private const double LongestGap = 0.5d;

    private double? lastTick;

    private double measured;

    private double anchor;

    private int ticks;

    /// <summary>
    /// Beats since the sequencer pressed Start, as of the latest tick. Holds while
    /// it is stopped, and jumps where it says it has moved.
    /// </summary>
    public float Beat => (float)(anchor + Math.Max(ticks - 1, 0) / (double)TicksPerBeat);

    /// <summary>Beats a second while running, and nought while stopped, so a line through the beat holds still.</summary>
    public float Rate => Running ? (float)measured : 0f;

    /// <summary>The tempo it is sending, whether or not it is running. Nought until two ticks have arrived.</summary>
    public float Bpm => (float)(measured * 60d);

    /// <summary>Between a Start or Continue and a Stop.</summary>
    public bool Running { get; private set; }

    /// <summary>
    /// How many times it has pressed Start. A count rather than a pulse, for the
    /// reason <see cref="MidiSignal.Strikes"/> is one: the program differences it
    /// into an edge at its own rate.
    /// </summary>
    public float Starts { get; private set; }

    /// <summary>
    /// A tick arrived at <paramref name="now"/>, in seconds on any steady clock.
    /// Counted while running, and measured for the tempo either way.
    /// </summary>
    public void Tick(double now)
    {
        if (lastTick is { } last)
        {
            var gap = now - last;

            if (gap > 0d && gap < LongestGap)
            {
                var heard = 1d / (TicksPerBeat * gap);

                measured = measured == 0d ? heard : measured + (heard - measured) * Smoothing;
            }
        }

        lastTick = now;

        // The first tick after a Start is the beat itself, so the count begins
        // moving on the second.
        if (Running) ticks++;
    }

    /// <summary>From the top: the beat is nought, and the tempo is whatever was last measured.</summary>
    public void Start()
    {
        Running = true;
        Starts++;
        anchor = 0d;
        ticks = 0;
    }

    /// <summary>From where it stopped.</summary>
    public void Continue() => Running = true;

    public void Stop() => Running = false;

    /// <summary>The song is now at <paramref name="sixteenths"/> sixteenth notes, running or not.</summary>
    public void Position(int sixteenths)
    {
        anchor = Math.Max(sixteenths, 0) / SixteenthsPerBeat;
        ticks = 0;
    }

    /// <summary>Puts this clock into a program's live-input block, under one instrument's name.</summary>
    public void WriteTo(LiveValues block, string source)
    {
        ArgumentNullException.ThrowIfNull(block);

        block.Set(MidiSignal.ClockKey(source, MidiSignal.Beat), Beat);
        block.Set(MidiSignal.ClockKey(source, MidiSignal.Rate), Rate);
        block.Set(MidiSignal.ClockKey(source, MidiSignal.Bpm), Bpm);
        block.Set(MidiSignal.ClockKey(source, MidiSignal.Running), Running ? 1f : 0f);
        block.Set(MidiSignal.ClockKey(source, MidiSignal.Starts), Starts);
    }
}
