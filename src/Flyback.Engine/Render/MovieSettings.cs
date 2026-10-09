namespace Flyback.Engine.Render;

/// <summary>What an export is asked for.</summary>
/// <param name="Seconds">
/// How long the clip runs. The one number this whole file exists to honor:
/// everything else has a defensible default and this does not, because a patch
/// is an endless function of time and only a person can say where to stop.
/// </param>
/// <param name="Quality">
/// 1 to 100, read as a JPEG quality by the format written here and as a rate
/// factor by the rest — see <see cref="ClipFormat.Crf"/>.
/// </param>
/// <param name="Format">
/// Which of <see cref="ClipFormats.Pictures"/> the file is. Null is the one this
/// program writes itself, which is what a machine with no ffmpeg has.
/// </param>
/// <param name="Ffmpeg">Where ffmpeg is, for a format that needs one.</param>
/// <param name="Oversample">How many times the output rate the sound is evaluated at.</param>
/// <param name="Input">What a Line In hears, or null for silence.</param>
/// <param name="From">
/// The second the clip starts at. The patch is played up to it unrecorded, so feedback, sequencers
/// and the sound arrive in the state they would be in.
/// </param>
public readonly record struct MovieSettings(
    int Width,
    int Height,
    double Seconds,
    double FramesPerSecond = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality,
    ClipFormat? Format = null,
    string? Ffmpeg = null,
    int Oversample = AudioRenderer.DefaultOversample,
    ILineInSource? Input = null,
    double From = 0d)
{
    /// <summary>Always at least one, so the shortest export is still a picture.</summary>
    public int FrameCount => Math.Max(1, (int)Math.Round(Seconds * FramesPerSecond));

    /// <summary>The frames played before the first one written.</summary>
    public int SkippedFrames => (int)Math.Round(From * FramesPerSecond);

    /// <summary>The format asked for, or the one written here.</summary>
    public ClipFormat Written => Format ?? ClipFormats.MotionJpegAvi;
}
