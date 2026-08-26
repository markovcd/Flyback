using Avalonia;
using Flyback.Core.Compile;

namespace Flyback.App.Controls;

/// <summary>
/// What the shell needs of a preview, whichever renderer is behind it. There are
/// two — the interpreter on the CPU and a shader on the GPU — and which one is
/// running is a property of the machine rather than of the patch, so the window
/// is deliberately not told.
/// </summary>
/// <remarks>
/// This is exactly the surface <see cref="PreviewSurface"/> already had. Frame
/// export is not on it: that is a static call on the CPU renderer and stays one,
/// because it runs headless on a thread with no graphics context and its output
/// is what the snapshot tests approve.
/// </remarks>
public interface IPreviewSurface
{
    /// <summary>
    /// Patch time in seconds. This is what the Time module reads. Settable so a
    /// host swapping one renderer for another can hand the new one the moment the
    /// old one was showing, rather than jumping the patch back to the start.
    /// </summary>
    double Time { get; set; }

    /// <summary>
    /// When set, the timeline is read from here instead of accumulated from
    /// wall-clock deltas — audio becomes the master clock while it is playing.
    /// </summary>
    Func<double>? Clock { get; set; }

    /// <summary>Cost of the last frame, for the status readout.</summary>
    double FrameMilliseconds { get; }

    PixelSize Resolution { get; set; }

    CompiledPatch Program { get; set; }

    /// <summary>
    /// What the patch is being played with while it is drawn. Sized from the
    /// program's own live inputs, so it is set alongside <see cref="Program"/>
    /// and never on its own.
    /// </summary>
    LiveValues Live { get; set; }

    /// <summary>
    /// Something outside the timeline changed and the picture is now out of date.
    /// </summary>
    /// <remarks>
    /// A key going down is the only thing that does this. Every other reason to
    /// redraw is either the clock moving or a property on this interface being
    /// set, and both are seen from inside; a note held while the clock is stopped
    /// is a change with no time behind it, and without this the picture would
    /// wait for the next edit to show it.
    /// </remarks>
    void Refresh();

    /// <summary>Rewinds to zero and clears the feedback history.</summary>
    void Rewind();
}
