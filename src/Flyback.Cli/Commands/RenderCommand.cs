using System.Globalization;
using Flyback.Cli.Models;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Flyback.Gpu;

namespace Flyback.Cli.Commands;

/// <summary>
/// Writes a patch to a file: a PNG of one moment, a sound file, or a clip of both.
/// Which one comes from the extension, because that is what the person naming the
/// file has already decided — see <see cref="ClipFormats"/> for the list, and
/// ADR-0089 for why some of them are ffmpeg's work and two are this program's own.
/// </summary>
/// <remarks>
/// The picture is drawn on the GPU through a headless context where there is one,
/// and on the processor where there is not or where asked; the two may differ in
/// their last bits (ADR-0035, ADR-0157). On the processor the programs run as IL,
/// which gives the interpreter's bytes faster.
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
        if (options is { Interpreted: true, Backend: PictureBackend.Gpu })
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: --interpreted draws on the processor, and --gpu asks for the GPU.");
            return Exit.Failed;
        }

        var still = options.Out.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

        if (!double.IsFinite(options.From) || options.From < 0d)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: --from is a second of the patch, from 0.");
            return Exit.Failed;
        }

        if (still && options.From > 0d)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: --from starts a clip or a sound; --at picks a still's moment.");
            return Exit.Failed;
        }

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
        // Read before anything is compiled, like ffmpeg above: a file that will not open is
        // worth knowing in a second.
        ILineInSource? heard = null;

        if (options.Input is { } input && !still)
        {
            var library = new SampleLibrary();

            if (library.Find(input.FullName) is not { } clip)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --input {input.Name}: {library.Explain(input.FullName)}");
                return Exit.Failed;
            }

            heard = RecordedLineIn.From(clip, GlobalConstants.SampleRate);
        }

        var wantsPicture = still || format!.HasPicture;
        var wantsSound = !still && (!format!.HasPicture || patch.Reaches().Sound);

        var video = wantsPicture ? patch.CompileForVideo(samples: samples, pictures: pictures) : null;

        // A still of a Scope, a Beam or a Meter is of the sound played up to it.
        if (still && video is not null && Listens(video.Program)) wantsSound = true;

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

        var refused = false;
        using var gpu = video is null ? null : Gpu(video.Program, options, error, out refused);

        if (refused) return Exit.Failed;

        IFrameRenderer frames = gpu is null ? new SynthRenderer() : gpu;

        if (!options.Interpreted)
        {
            // The picture is drawn in stages and the sound whole, as in the app.
            var failures = new[]
            {
                video is null || gpu is not null ? null : IlCompiler.CompileOnce(video.Program, IlParts.Staged),
                audio is null ? null : IlCompiler.CompileOnce(audio.Program, IlParts.Whole),
            };

            foreach (var failure in failures.OfType<string>().Distinct())
                error.WriteLine($"{GlobalConstants.ApplicationName}: warning: {failure}");
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
                Still(video!.Program, audio?.Program, frames, options, heard);
                code = Exit.Ok;
            }
            else if (!format!.HasPicture)
            {
                Sound(audio!.Program, format, options, ffmpeg, loudness, heard);
                code = Exit.Ok;
            }
            else
            {
                code = Movie(video!.Program, audio?.Program, frames, format, options, ffmpeg, error, progress, loudness, heard, cancellation);
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

    /// <summary>
    /// The GPU's renderer with <paramref name="program"/>'s shader built, or null to
    /// draw on the processor. <paramref name="refused"/> when --gpu was asked for
    /// and there is none; said on <paramref name="error"/> either way.
    /// </summary>
    private static HeadlessRenderer? Gpu(CompiledPatch program, RenderOptions options, TextWriter error, out bool refused)
    {
        refused = false;

        if (options.Interpreted || options.Backend == PictureBackend.Processor) return null;

        var gpu = HeadlessRenderer.Open(out var why);

        if (gpu?.Prepare(program) is { } failure)
        {
            gpu.Dispose();
            gpu = null;
            why = failure;
        }

        if (gpu is not null) return gpu;

        if (options.Backend == PictureBackend.Gpu)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: --gpu: {why}");
            refused = true;
        }
        else
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: warning: {why} Drawing on the processor.");
        }

        return null;
    }

    // Whether the picture reads what the speakers played: a chart's buffer or a Meter's reading.
    private static bool Listens(CompiledPatch program) =>
        program.Taps.Count > 0 || program.LiveInputs.Any(MeterSignals.Is);

    private static void Still(
        CompiledPatch program, CompiledPatch? audio, IFrameRenderer frames, RenderOptions options, ILineInSource? input)
    {
        var stride = options.Width * 4;
        var pixels = new byte[stride * options.Height];
        var heard = new LiveValues(program.LiveInputs);

        if (audio is not null && options.At > 0d)
        {
            var speaker = new AudioRenderer(oversample: options.Oversample)
            {
                Aspect = SynthRenderer.AspectOf(options.Width, options.Height),
                Input = input,
            };

            Play(speaker, audio, options.At);

            Meters.Refresh(audio, speaker.Memory, heard);
            Traces.Refresh(program, audio, speaker.Memory);
        }

        frames.Render(program, options.At, options.Width, options.Height, pixels, stride, heard);

        PngWriter.WriteBgra(options.Out.FullName, pixels, options.Width, options.Height, stride);
    }

    /// <summary>Plays <paramref name="program"/> through <paramref name="speaker"/> for <paramref name="seconds"/> and keeps none of it.</summary>
    private static void Play(AudioRenderer speaker, CompiledPatch program, double seconds)
    {
        // A second at a time, so a start an hour in does not hold the hour.
        var left = (long)Math.Round(seconds * speaker.SampleRate);
        var chunk = new float[speaker.SampleRate * NodeCatalog.AudioChannels];

        for (; left > 0; left -= speaker.SampleRate)
            speaker.Render(program, chunk.AsSpan(0, (int)Math.Min(left, speaker.SampleRate) * NodeCatalog.AudioChannels));
    }

    private static void Sound(
        CompiledPatch program, ClipFormat format, RenderOptions options, string? ffmpeg, LoudnessMeter? loudness, ILineInSource? heard)
    {
        // Nothing is drawn, but a patch reading Coordinates' aspect is still told
        // the frame it would have been drawn at.
        var renderer = new AudioRenderer(oversample: options.Oversample) { Aspect = SynthRenderer.AspectOf(options.Width, options.Height), Input = heard };

        Play(renderer, program, options.From);

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
        IFrameRenderer frames,
        ClipFormat format,
        RenderOptions options,
        string? ffmpeg,
        TextWriter error,
        IProgress<double>? progress,
        LoudnessMeter? loudness,
        ILineInSource? heard,
        CancellationToken cancellation)
    {
        var settings = new MovieSettings(
            options.Width, options.Height, options.Seconds, options.Fps, options.Quality, format, ffmpeg, options.Oversample, heard, options.From);

        // Silence is not worth a track. A patch with nothing in its 'left' is
        // compiled for the eye only above, and gets a clip with no audio stream
        // rather than one full of zeroes.
        var written = MovieRenderer.Render(
            options.Out.FullName,
            video,
            audio,
            settings,
            frames,
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
