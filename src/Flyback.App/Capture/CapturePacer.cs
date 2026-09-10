namespace Flyback.App.Capture;

/// <summary>
/// Decides when a frame is due. The preview drops frames to hold its clock and an
/// AVI is a constant-rate file, so something has to stand between the two.
/// </summary>
/// <remarks>
/// Frame <c>n</c> belongs at <c>n / rate</c> seconds and the file is never allowed
/// to disagree: asked what is due at the moment the sound has reached, this
/// answers with however many frames that moment has passed. A recorder answers
/// "more than one" by writing the same frame again, which is what keeps the file
/// in step with the sound — letting the rate float instead produces a file that
/// drifts out of sync with its own audio.
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
    /// the clock sits between two frames. Asking does not commit, so a caller with
    /// nothing to write can simply not <see cref="Commit"/>: marking frames written
    /// before they are is how a file ends up short of its own sound track.
    /// </summary>
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
