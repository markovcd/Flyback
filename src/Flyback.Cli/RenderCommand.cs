using System.Globalization;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Cli;

/// <summary>Everything about a render that is not the patch.</summary>
/// <param name="At">Which moment a still is of. Ignored by the two that have a length instead.</param>
/// <param name="Format">
/// Which of <see cref="ClipFormats"/> to write, by id, or null to take it from the
/// extension of <paramref name="Out"/>.
/// </param>
/// <param name="Ffmpeg">
/// Where ffmpeg is, for a format that needs it. Null looks on <c>PATH</c>.
/// </param>
/// <param name="Loudness">
/// Whether to measure the sound as it is written and say how loud it came out.
/// Ignored for a still, which has none.
/// </param>
internal sealed record RenderOptions(
    FileInfo Out,
    int Width = 1920,
    int Height = 1080,
    double At = 0d,
    double Seconds = 10d,
    double Fps = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality,
    string? Format = null,
    string? Ffmpeg = null,
    bool Loudness = false);

/// <summary>
/// Writes a patch to a file: a PNG of one moment, a sound file, or a clip of both.
/// Which one comes from the extension, because that is what the person naming the
/// file has already decided — see <see cref="ClipFormats"/> for the list, and
/// ADR-0089 for why some of them are ffmpeg's work and two are this program's own.
/// </summary>
/// <remarks>
/// Always the interpreter, never the shader backend: a GPU render needs a context and
/// a window, and the two backends are allowed to differ in their last bits
/// (ADR-0035), so the one that can be run here is also the one whose output is the
/// same bytes every time.
/// </remarks>
internal static class RenderCommand
{
    public static int Run(
        Patch patch,
        RenderOptions options,
        TextWriter error,
        IProgress<double>? progress = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        TextWriter? output = null,
        CancellationToken cancellation = default)
    {
        var still = options.Out.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

        var asked = still ? null : options.Format;

        if (asked is not null && ClipFormats.ById(asked) is null)
        {
            var ids = string.Join(", ", ClipFormats.All.Select(f => f.Id));

            error.WriteLine($"{GlobalConstants.ApplicationName}: --format {asked}: choose one of {ids}.");
            return Exit.Failed;
        }

        var format = still
            ? null
            : ClipFormats.ById(asked) ?? ClipFormats.ByExtension(options.Out.Name);

        // ffmpeg takes the container from the name, so one of its formats under
        // another extension is its business. A format written here has one
        // container, and under any other name is a file that lies about itself.
        if (format is { NeedsFfmpeg: false }
            && !options.Out.Extension.Equals(format.Extension, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: {options.Out.Name}: {format.Label} goes in a "
                + $"{format.Extension} file.");

            return Exit.Failed;
        }

        if (!still && format is null)
        {
            var known = string.Join(", ", ClipFormats.All.Select(f => f.Extension).Distinct());

            error.WriteLine($"{GlobalConstants.ApplicationName}: {options.Out.Name}: write a .png or one of {known}.");
            return Exit.Failed;
        }

        // Resolved before anything is compiled, so a machine with no ffmpeg is
        // told so in a second rather than after rendering a clip it cannot write.
        var ffmpeg = format?.NeedsFfmpeg == true ? Ffmpeg.Resolve(options.Ffmpeg) : null;

        if (format?.NeedsFfmpeg == true && ffmpeg is null)
        {
            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: {format.Label} is written by ffmpeg, and there is none "
                + "on PATH. Install it, point --ffmpeg at it, or ask for a "
                + $"{ClipFormats.MotionJpegAvi.Extension} or {ClipFormats.Wav.Extension} instead.");

            return Exit.Failed;
        }

        // Only the half being written. A patch wired for the eye and not the ear
        // has plenty to say about its audio program, and none of it is worth
        // saying to somebody asking for a picture.
        var wantsPicture = still || format!.HasPicture;
        var wantsSound = !still && (!format!.HasPicture || patch.Reaches().Sound);

        var video = wantsPicture ? patch.CompileForVideo(samples: samples, pictures: pictures) : null;
        var audio = wantsSound ? patch.CompileForAudio(samples: samples) : null;

        var issues = (video?.Issues ?? [])
            .Concat(audio?.Issues ?? [])
            .DistinctBy(i => (i.NodeId, i.Message))
            .ToArray();

        foreach (var issue in issues)
            error.WriteLine($"{GlobalConstants.ApplicationName}: {(issue.Severity == IssueSeverity.Error ? "error" : "warning")}: {issue.Message}");

        // A warning is a patch somebody may have meant, so it is said and the
        // file is written anyway. An error means part of what compiled is a
        // stand-in, and a file made of stand-ins looks exactly like a real one.
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: refusing to render a patch with errors in it.");
            return Exit.Problems;
        }

        // Measured as it is written, so a loudness report costs no second pass
        // over a sound that may be an hour long.
        var loudness = options.Loudness && audio is not null
            ? new LoudnessMeter(GlobalConstants.SampleRate, NodeCatalog.AudioChannels)
            : null;

        int code;

        try
        {
            if (still)
            {
                Still(video!.Program, options);
                code = Exit.Ok;
            }
            else if (!format!.HasPicture)
            {
                Sound(audio!.Program, format, options, ffmpeg, loudness);
                code = Exit.Ok;
            }
            else
            {
                code = Movie(video!.Program, audio?.Program, format, options, ffmpeg, error, progress, loudness, cancellation);
            }
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {options.Out.Name}: {ex.Message}");
            return Exit.Failed;
        }

        if (options.Loudness) Report(loudness, output ?? Console.Out);

        return code;
    }

    /// <summary>
    /// The loudness line: integrated loudness and true peak, the two numbers a
    /// streaming service's delivery spec asks for.
    /// </summary>
    private static void Report(LoudnessMeter? loudness, TextWriter output)
    {
        if (loudness is null)
        {
            output.WriteLine("loudness: no sound to measure");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"loudness: {Decibels(loudness.Integrated)} LUFS integrated, "
            + $"{Decibels(20d * Math.Log10(loudness.TruePeak))} dBTP true peak"));

        static string Decibels(double value) =>
            double.IsFinite(value) ? value.ToString("0.0", CultureInfo.InvariantCulture) : "-inf";
    }

    private static void Still(CompiledPatch program, RenderOptions options)
    {
        var stride = options.Width * 4;
        var pixels = new byte[stride * options.Height];

        new SynthRenderer().Render(program, options.At, options.Width, options.Height, pixels, stride);

        PngWriter.WriteBgra(options.Out.FullName, pixels, options.Width, options.Height, stride);
    }

    private static void Sound(
        CompiledPatch program, ClipFormat format, RenderOptions options, string? ffmpeg, LoudnessMeter? loudness)
    {
        // Nothing is drawn, but a patch reading Coordinates' aspect is still told
        // the frame it would have been drawn at.
        var renderer = new AudioRenderer { Aspect = SynthRenderer.AspectOf(options.Width, options.Height) };
        var frames = (int)Math.Round(renderer.SampleRate * options.Seconds);
        var samples = new float[frames * NodeCatalog.AudioChannels];

        renderer.Render(program, samples);
        loudness?.Add(samples);

        // Rendered whole before a file is opened, unlike a clip: the sound of a
        // patch is one array, and there is nothing to be gained by handing an
        // encoder a tenth of it at a time.
        using var clip = ClipWriter.Open(new ClipTarget(
            options.Out.FullName,
            format,
            SampleRate: renderer.SampleRate,
            Channels: NodeCatalog.AudioChannels,
            Ffmpeg: ffmpeg));

        clip.WriteAudio(samples);
    }

    private static int Movie(
        CompiledPatch video,
        CompiledPatch? audio,
        ClipFormat format,
        RenderOptions options,
        string? ffmpeg,
        TextWriter error,
        IProgress<double>? progress,
        LoudnessMeter? loudness,
        CancellationToken cancellation)
    {
        var settings = new MovieSettings(
            options.Width, options.Height, options.Seconds, options.Fps, options.Quality, format, ffmpeg);

        // Silence is not worth a track. A patch with nothing in its 'left' is
        // compiled for the eye only above, and gets a clip with no audio stream
        // rather than one full of zeroes.
        var written = MovieRenderer.Render(
            options.Out.FullName,
            video,
            audio,
            settings,
            progress,
            loudness,
            cancellation);

        if (written >= settings.FrameCount) return Exit.Ok;

        // Stopped partway. The file is whole and shorter, which is worth saying
        // plainly rather than leaving to be discovered on playback.
        error.WriteLine(
            $"{GlobalConstants.ApplicationName}: stopped after {written} of {settings.FrameCount} frames — "
            + $"{options.Out.Name} holds the {written / settings.FramesPerSecond:0.0}s already rendered.");

        return Exit.Failed;
    }
}
