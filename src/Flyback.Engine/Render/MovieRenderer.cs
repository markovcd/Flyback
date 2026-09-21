using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Render;

/// <summary>What an export is asked for.</summary>
/// <param name="Seconds">
/// How long the clip runs. The one number this whole file exists to honour:
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
public readonly record struct MovieSettings(
    int Width,
    int Height,
    double Seconds,
    double FramesPerSecond = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality,
    ClipFormat? Format = null,
    string? Ffmpeg = null)
{
    /// <summary>Always at least one, so the shortest export is still a picture.</summary>
    public int FrameCount => Math.Max(1, (int)Math.Round(Seconds * FramesPerSecond));

    /// <summary>The format asked for, or the one written here.</summary>
    public ClipFormat Written => Format ?? ClipFormats.MotionJpegAvi;
}

/// <summary>
/// Renders both sinks of a patch to one file: the video program frame by frame,
/// the audio program sample by sample, into whichever
/// <see cref="ClipFormat"/> was asked for.
/// </summary>
/// <remarks>
/// Offline, so time is taken from the frame number rather than a stopwatch and a
/// slow patch takes longer to write than to watch. One
/// <see cref="SynthRenderer"/> for the whole run, which is what makes Feedback
/// mean anything here; the audio side keeps its own cursor for the same reason.
/// </remarks>
public static class MovieRenderer
{
    /// <summary>
    /// Fast enough that motion reads as motion, slow enough that a patch costing
    /// 25 ms a frame exports in about the time it would take to watch it.
    /// </summary>
    public const double DefaultFrameRate = 30d;

    /// <summary>
    /// Renders to a file, in whichever format <paramref name="settings"/> names.
    /// The only entry that can reach a format ffmpeg writes, since ffmpeg is
    /// given somewhere to write rather than something to write into.
    /// </summary>
    /// <inheritdoc cref="Render(Stream, CompiledPatch, CompiledPatch, MovieSettings, IProgress{double}, CancellationToken)"/>
    /// <param name="loudness">Fed every sample of the sound as it is written, or null to measure nothing.</param>
    public static int Render(
        string path,
        CompiledPatch video,
        CompiledPatch? audio,
        MovieSettings settings,
        IProgress<double>? progress = null,
        LoudnessMeter? loudness = null,
        CancellationToken cancellation = default)
    {
        Check(settings);

        using var clip = ClipWriter.Open(new ClipTarget(
            path,
            settings.Written,
            settings.Width,
            settings.Height,
            settings.FramesPerSecond,
            settings.Quality,
            audio is null ? 0 : GlobalConstants.SampleRate,
            audio is null ? 0 : NodeCatalog.AudioChannels,
            settings.Ffmpeg));

        return Render(clip, video, audio, settings, progress, loudness, cancellation);
    }

    /// <param name="video">The picture's compiled program, rooted at the Output's color.</param>
    /// <param name="audio">
    /// Null writes a video-only file, which is what a patch with nothing
    /// reaching the Output's 'left' has to say — see <see cref="Graph.Patch.Reaches"/>,
    /// which is how a caller decides.
    /// </param>
    /// <param name="progress">Told how far along this is, 0 to 1, once a frame. Null asks for nothing.</param>
    /// <param name="cancellation">
    /// Stops at the next frame boundary rather than throwing. What has been
    /// rendered is kept and the file is closed properly, so stopping a long
    /// export leaves a shorter video rather than a broken one.
    /// </param>
    /// <param name="output">
    /// Where the file is written. Always Motion JPEG in an AVI — the one format
    /// written here, and so the only one a stream can hold.
    /// </param>
    /// <param name="settings">Size, length, rate and quality: everything about the file that is not a program.</param>
    /// <returns>Frames written — fewer than <see cref="MovieSettings.FrameCount"/> if stopped.</returns>
    public static int Render(
        Stream output,
        CompiledPatch video,
        CompiledPatch? audio,
        MovieSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        Check(settings);

        using var clip = new AviClipWriter(output, new ClipTarget(
            string.Empty,
            ClipFormats.MotionJpegAvi,
            settings.Width,
            settings.Height,
            settings.FramesPerSecond,
            settings.Quality,
            audio is null ? 0 : GlobalConstants.SampleRate,
            audio is null ? 0 : NodeCatalog.AudioChannels));

        return Render(clip, video, audio, settings, progress, null, cancellation);
    }

    /// <summary>Everything that has to be true of a clip before a file is opened for it.</summary>
    private static void Check(MovieSettings settings)
    {
        if (settings.Width <= 0 || settings.Height <= 0) throw new ArgumentOutOfRangeException(nameof(settings), "A frame needs both dimensions.");
        if (settings.FramesPerSecond <= 0d) throw new ArgumentOutOfRangeException(nameof(settings), "A frame rate has to be positive.");
        if (settings.Seconds <= 0d) throw new ArgumentOutOfRangeException(nameof(settings), "An export has to have a length.");
        if (!settings.Written.HasPicture) throw new ArgumentOutOfRangeException(nameof(settings), "A clip of a patch has a picture in it.");
    }

    /// <summary>
    /// The loop itself, which is the same whoever encodes what comes out of it.
    /// What a format costs is <see cref="IClipWriter"/>'s business and changes
    /// nothing about the order the two sinks are advanced in.
    /// </summary>
    private static int Render(
        IClipWriter clip,
        CompiledPatch video,
        CompiledPatch? audio,
        MovieSettings settings,
        IProgress<double>? progress,
        LoudnessMeter? loudness,
        CancellationToken cancellation)
    {
        var width = settings.Width;
        var height = settings.Height;

        var total = settings.FrameCount;
        var rate = settings.FramesPerSecond;
        var stride = width * 4;

        var frames = new SynthRenderer();
        var pixels = new byte[(long)stride * height];

        // The sound of this frame, so a Scan crossing the width crosses the one
        // being written.
        var speaker = audio is null
            ? null
            : new AudioRenderer { Aspect = SynthRenderer.AspectOf(width, height) };
        var samples = Array.Empty<float>();
        var written = 0L;

        // What the picture is told about the sound — a Meter's reading, and
        // nothing else offline, since nobody is playing a keyboard into a file.
        // Empty for a patch with no Meter in it, which is the great majority, and
        // then this whole apparatus is one allocation of nothing.
        var heard = new LiveValues(video.LiveInputs);

        for (var frame = 0; frame < total; frame++)
        {
            if (cancellation.IsCancellationRequested) break;

            var sounded = 0;

            if (speaker is not null && audio is not null)
            {
                // Where the sound should have reached by the end of this frame,
                // less where it already has. At 30 fps into 48 kHz that is a
                // flat 1600 samples; at 29.97 it alternates, and over a long
                // export those single samples are the difference between the two
                // streams ending together and drifting apart.
                var due = (long)Math.Round((frame + 1) / rate * speaker.SampleRate);
                var count = (int)(due - written);

                if (count > 0)
                {
                    sounded = count * NodeCatalog.AudioChannels;
                    if (samples.Length < sounded) samples = new float[sounded];

                    speaker.Render(audio, samples.AsSpan(0, sounded));
                    loudness?.Add(samples.AsSpan(0, sounded));
                    written = due;
                }

                // Before the frame rather than after it, which is the one place
                // this differs from the preview and is the better answer of the
                // two. On screen a Meter reads what was played up to now, because
                // now is the only thing there is; here the whole clip exists, so
                // a frame can be lit by the sound it is played with rather than
                // by the sound before it. Nothing else in the loop moved: the
                // samples are still counted from the frame number, and they are
                // still written to the file after the picture.
                Meters.Refresh(audio, speaker.Memory, heard);

                // And a Scope or an Analyzer is refilled from the same rings, so
                // a chart in a clip is a chart of the clip's own sound.
                Traces.Refresh(video, audio, speaker.Memory);
            }

            // From the frame number, not from an accumulated delta: a rounding
            // error repeated a few thousand times is audible against a sound
            // track that counts its own samples exactly.
            frames.Render(video, frame / rate, width, height, pixels, stride, heard);

            clip.WriteFrame(pixels, stride);

            if (sounded > 0) clip.WriteAudio(samples.AsSpan(0, sounded));

            progress?.Report((frame + 1) / (double)total);
        }

        return (int)clip.FrameCount;
    }
}
