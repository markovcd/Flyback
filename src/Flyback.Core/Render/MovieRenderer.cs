using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Render;

/// <summary>What an export is asked for.</summary>
/// <param name="Seconds">
/// How long the clip runs. The one number this whole file exists to honour:
/// everything else has a defensible default and this does not, because a patch
/// is an endless function of time and only a person can say where to stop.
/// </param>
/// <param name="Quality">JPEG quality, 1 to 100. See <see cref="JpegWriter"/>.</param>
public readonly record struct MovieSettings(
    int Width,
    int Height,
    double Seconds,
    double FramesPerSecond = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality)
{
    /// <summary>Always at least one, so the shortest export is still a picture.</summary>
    public int FrameCount => Math.Max(1, (int)Math.Round(Seconds * FramesPerSecond));
}

/// <summary>
/// Renders both sinks of a patch to one file: the video program frame by frame
/// as Motion JPEG, the audio program sample by sample as PCM, interleaved into
/// an AVI.
/// </summary>
/// <remarks>
/// Offline, and deliberately nothing like the preview. The preview drops frames
/// to keep a clock; this cannot, so time is taken from the frame number rather
/// than from a stopwatch and a slow patch simply takes longer to write than it
/// does to watch.
///
/// One <see cref="SynthRenderer"/> for the whole run, which is what makes
/// Feedback mean anything here — each frame reads the one before it, exactly as
/// on screen, and exporting frame by frame through <c>SaveFrame</c> could never
/// have shown that. The audio side keeps its own cursor for the same reason: an
/// oscillator's phase and a delay line's tail run the length of the clip.
/// </remarks>
public static class MovieRenderer
{
    /// <summary>
    /// Fast enough that motion reads as motion, slow enough that a patch costing
    /// 25 ms a frame exports in about the time it would take to watch it.
    /// </summary>
    public const double DefaultFrameRate = 30d;

    /// <inheritdoc cref="Render(Stream, CompiledPatch, CompiledPatch, AudioScan, MovieSettings, IProgress{double}, CancellationToken)"/>
    public static int Render(
        string path,
        CompiledPatch video,
        CompiledPatch? audio,
        AudioScan scan,
        MovieSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        using var file = File.Create(path);

        return Render(file, video, audio, scan, settings, progress, cancellation);
    }

    /// <param name="video">The picture's compiled program, rooted at the Output's colour.</param>
    /// <param name="audio">
    /// Null writes a video-only file, which is what a patch with no Audio Output
    /// has to say.
    /// </param>
    /// <param name="progress">Told how far along this is, 0 to 1, once a frame. Null asks for nothing.</param>
    /// <param name="cancellation">
    /// Stops at the next frame boundary rather than throwing. What has been
    /// rendered is kept and the file is closed properly, so stopping a long
    /// export leaves a shorter video rather than a broken one.
    /// </param>
    /// <param name="output">Where the file is written.</param>
    /// <param name="scan">Passed to the audio renderer unchanged — see <see cref="AudioScan"/>.</param>
    /// <param name="settings">Size, length, rate and quality: everything about the file that is not a program.</param>
    /// <returns>Frames written — fewer than <see cref="MovieSettings.FrameCount"/> if stopped.</returns>
    public static int Render(
        Stream output,
        CompiledPatch video,
        CompiledPatch? audio,
        AudioScan scan,
        MovieSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        var width = settings.Width;
        var height = settings.Height;

        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(settings), "A frame needs both dimensions.");
        if (settings.FramesPerSecond <= 0d) throw new ArgumentOutOfRangeException(nameof(settings), "A frame rate has to be positive.");
        if (settings.Seconds <= 0d) throw new ArgumentOutOfRangeException(nameof(settings), "An export has to have a length.");

        var total = settings.FrameCount;
        var rate = settings.FramesPerSecond;
        var stride = width * 4;

        var frames = new SynthRenderer();
        var pixels = new byte[(long)stride * height];

        var jpeg = new JpegWriter(settings.Quality);
        var encoded = new MemoryStream(width * height / 4);

        var speaker = audio is null ? null : new AudioRenderer();
        var samples = Array.Empty<float>();
        var written = 0L;

        using var avi = new AviWriter(
            output,
            width,
            height,
            rate,
            speaker?.SampleRate ?? 0,
            speaker is null ? 0 : NodeCatalog.AudioChannels);

        for (var frame = 0; frame < total; frame++)
        {
            if (cancellation.IsCancellationRequested) break;

            // From the frame number, not from an accumulated delta: a rounding
            // error repeated a few thousand times is audible against a sound
            // track that counts its own samples exactly.
            frames.Render(video, frame / rate, width, height, pixels, stride);

            encoded.SetLength(0);
            jpeg.WriteBgra(encoded, pixels, width, height, stride);
            avi.WriteFrame(encoded.GetBuffer().AsSpan(0, (int)encoded.Length));

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
                    var wanted = count * NodeCatalog.AudioChannels;
                    if (samples.Length < wanted) samples = new float[wanted];

                    var slice = samples.AsSpan(0, wanted);
                    speaker.Render(audio, slice, scan);
                    avi.WriteAudio(slice);

                    written = due;
                }
            }

            progress?.Report((frame + 1) / (double)total);
        }

        return avi.FrameCount;
    }
}
