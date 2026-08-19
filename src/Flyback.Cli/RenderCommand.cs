using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Cli;

/// <summary>Everything about a render that is not the patch.</summary>
/// <param name="At">Which moment a still is of. Ignored by the two that have a length instead.</param>
internal sealed record RenderOptions(
    FileInfo Out,
    int Width = 1920,
    int Height = 1080,
    double At = 0d,
    double Seconds = 10d,
    double Fps = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality);

/// <summary>
/// Writes a patch to a file: a PNG of one moment, a WAV of the sound, or an AVI
/// of both.
/// </summary>
/// <remarks>
/// The one thing the shell could do that nothing else could, done without the
/// shell. Which of the three it is comes from the extension, because that is
/// what the person naming the file has already decided.
/// <para>
/// Always the interpreter, never the shader backend. That is not a limitation
/// worked around — a GPU render needs a context and a window, and the two
/// backends are allowed to differ in their last bits (ADR-0035), so the one that
/// can be run here is also the one whose output is the same bytes every time.
/// </para>
/// </remarks>
internal static class RenderCommand
{
    public static int Run(
        Patch patch,
        RenderOptions options,
        TextWriter error,
        IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        var kind = options.Out.Extension.ToLowerInvariant();

        if (kind is not (".png" or ".wav" or ".avi"))
        {
            error.WriteLine($"flyback: {options.Out.Name}: write a .png, a .wav or an .avi.");
            return Exit.Failed;
        }

        // Only the half being written. A patch wired for the eye and not the ear
        // has plenty to say about its audio program, and none of it is worth
        // saying to somebody asking for a picture.
        var wantsPicture = kind is ".png" or ".avi";
        var wantsSound = kind is ".wav" or ".avi";

        var video = wantsPicture ? patch.CompileForVideo() : null;
        var audio = wantsSound ? patch.CompileForAudio() : null;

        var issues = (video?.Issues ?? [])
            .Concat(audio?.Issues ?? [])
            .DistinctBy(i => (i.NodeId, i.Message))
            .ToArray();

        foreach (var issue in issues)
            error.WriteLine($"flyback: {(issue.Severity == IssueSeverity.Error ? "error" : "warning")}: {issue.Message}");

        // A warning is a patch somebody may have meant, so it is said and the
        // file is written anyway. An error means part of what compiled is a
        // stand-in, and a file made of stand-ins looks exactly like a real one.
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            error.WriteLine("flyback: refusing to render a patch with errors in it.");
            return Exit.Problems;
        }

        try
        {
            switch (kind)
            {
                case ".png":
                    Still(video!.Program, options);
                    break;

                case ".wav":
                    Sound(patch, audio!.Program, options);
                    break;

                default:
                    return Movie(patch, video!.Program, audio!.Program, options, error, progress, cancellation);
            }
        }
        catch (Exception ex)
        {
            error.WriteLine($"flyback: {options.Out.Name}: {ex.Message}");
            return Exit.Failed;
        }

        return Exit.Ok;
    }

    private static void Still(CompiledPatch program, RenderOptions options)
    {
        var stride = options.Width * 4;
        var pixels = new byte[stride * options.Height];

        new SynthRenderer().Render(program, options.At, options.Width, options.Height, pixels, stride);

        PngWriter.WriteBgra(options.Out.FullName, pixels, options.Width, options.Height, stride);
    }

    private static void Sound(Patch patch, CompiledPatch program, RenderOptions options)
    {
        var renderer = new AudioRenderer();
        var frames = (int)Math.Round(renderer.SampleRate * options.Seconds);
        var samples = new float[frames * NodeCatalog.AudioChannels];

        renderer.Render(program, samples, Scan(patch, options));

        WavWriter.Write(options.Out.FullName, samples, renderer.SampleRate, NodeCatalog.AudioChannels);
    }

    private static int Movie(
        Patch patch,
        CompiledPatch video,
        CompiledPatch audio,
        RenderOptions options,
        TextWriter error,
        IProgress<double>? progress,
        CancellationToken cancellation)
    {
        var settings = new MovieSettings(
            options.Width, options.Height, options.Seconds, options.Fps, options.Quality);

        // Silence is not worth a track. A patch with nothing in its 'left' would
        // otherwise get one full of zeroes, which is a bigger file saying less.
        var written = MovieRenderer.Render(
            options.Out.FullName,
            video,
            patch.Reaches().Sound ? audio : null,
            Scan(patch, options),
            settings,
            progress,
            cancellation);

        if (written >= settings.FrameCount) return Exit.Ok;

        // Stopped partway. The file is whole and shorter, which is worth saying
        // plainly rather than leaving to be discovered on playback.
        error.WriteLine(
            $"flyback: stopped after {written} of {settings.FrameCount} frames — "
            + $"{options.Out.Name} holds the {written / settings.FramesPerSecond:0.0}s already rendered.");

        return Exit.Failed;
    }

    /// <summary>
    /// How the sound is driven, over the frame this render is of — a scanned
    /// patch sweeps the width being written rather than some other width.
    /// </summary>
    private static AudioScan Scan(Patch patch, RenderOptions options) =>
        AudioScan.For(patch, SynthRenderer.AspectOf(options.Width, options.Height));
}
