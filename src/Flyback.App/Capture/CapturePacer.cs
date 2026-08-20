namespace Flyback.App.Capture;

/// <summary>
/// Decides when a frame is due. The preview drops frames to hold its clock and
/// an AVI is a constant-rate file, so something has to stand between the two.
/// </summary>
/// <remarks>
/// The rule is that frame <c>n</c> belongs at <c>n / rate</c> seconds and the
/// file is never allowed to disagree. Ask what is due at the moment the sound
/// has reached, and this answers with however many frames that moment has
/// passed — usually one, none while the clock is between frames, and more than
/// one when the picture fell behind.
/// <para>
/// A recorder that answers "more than one" by writing the same frame again is
/// what keeps the file in step with the sound. The alternative, writing a frame
/// per frame rendered and letting the rate float, produces a file that drifts
/// out of sync with its own audio and is the one thing a recording of a
/// performance cannot do.
/// </para>
/// </remarks>
internal sealed class CapturePacer
{
    private readonly double framesPerSecond;

    private long emitted;

    public CapturePacer(double framesPerSecond)
    {
        if (framesPerSecond <= 0d) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));

        this.framesPerSecond = framesPerSecond;
    }

    /// <summary>Frames handed out so far, duplicates included — the file's length.</summary>
    public long Emitted => emitted;

    /// <summary>
    /// How many frames the file owes as of <paramref name="seconds"/>. Zero while
    /// the clock sits between two frames.
    /// </summary>
    /// <remarks>
    /// Asking does not commit, so a caller that finds it has nothing to write can
    /// simply not <see cref="Commit"/> and be asked again a moment later. Marking
    /// frames written before they are is how a file ends up short of its own
    /// sound track.
    /// </remarks>
    public int Due(double seconds)
    {
        if (double.IsNaN(seconds) || seconds <= 0d) return 0;

        // Floor rather than round: frame n is due once the clock has actually
        // reached n / rate, never before it.
        var due = (long)Math.Floor(seconds * framesPerSecond) - emitted;

        return due <= 0 ? 0 : (int)due;
    }

    /// <summary>Records that many frames as written.</summary>
    public void Commit(int count)
    {
        if (count > 0) emitted += count;
    }
}
