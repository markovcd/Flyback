using System.CommandLine;
using System.CommandLine.Completions;
using System.Globalization;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Gpu;
using Flyback.Host;
using PluginRegistry = Flyback.Cli.Plugins;

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
    public static Command Build(PluginRegistry plugins, OutputSettings defaults)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}. Left out, give --preset instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var presets = new Option<bool>("--presets")
        {
            Description = "List what --preset would accept, and stop.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = "Where to write it. The extension picks the format: .png for a still, "
                + string.Join(", ", ClipFormats.All.Select(f => f.Extension).Distinct())
                + " for the rest.",
        };

        var size = new Option<(int Width, int Height)>("--size")
        {
            Description = "Frame size, as WIDTHxHEIGHT, or " + string.Join(", ", Resolutions.Names.Keys) + ".",
            DefaultValueFactory = _ => (defaults.Width, defaults.Height),
            CustomParser = SizeArgument.Parse,
        };

        var at = new Option<double>("--at")
        {
            Description = "Which second of the patch a still is of.",
        };

        var seconds = new Option<double>("--seconds")
        {
            Description = "How long a clip runs. A patch is endless, so only you can say.",
            DefaultValueFactory = _ => 10d,
        };

        var from = new Option<double>("--from")
        {
            Description = "Which second a clip or a sound starts at. The patch is played up to it unrecorded, "
                + "so feedback and sequencers are in the state they would be in; --seconds counts from there.",
        };

        var fps = new Option<double>("--fps")
        {
            Description = "Frames a second, for a clip.",
            DefaultValueFactory = _ => defaults.FrameRate,
        };

        var quality = new Option<int>("--quality")
        {
            Description = "How good the picture is, 1 to 100 — a JPEG quality in an AVI, "
                + "and a rate factor everywhere else.",
            DefaultValueFactory = _ => defaults.JpegQuality,
        };

        var format = new Option<string>("--format")
        {
            Description = "Write this format rather than the one the extension names: "
                + string.Join(", ", ClipFormats.All.Select(f => f.Id)) + ".",
        };

        format.CompletionSources.Add(_ => ClipFormats.All.Select(f => new CompletionItem(f.Id, f.Label)));

        var ffmpeg = new Option<string>("--ffmpeg")
        {
            Description = $"The ffmpeg to encode with. Left out, the one the editor's settings name, or else "
                + $"the first on PATH; only {ClipFormats.MotionJpegAvi.Id} and {ClipFormats.Wav.Id} need none at all.",
        };

        var settings = new Option<string>("--settings")
        {
            HelpName = "path",
            Description = "Read the defaults from another settings.json than the editor's.",
        };

        var loudness = new Option<bool>("--loudness")
        {
            Description = "Say how loud the sound came out: integrated loudness in LUFS and true peak "
                + "in dBTP, measured as ITU-R BS.1770 does.",
        };

        var interpreted = new Option<bool>("--interpreted")
        {
            Description = "Keep the patch on the interpreter rather than compiling it. Same bytes, slower.",
        };

        var gpu = new Option<bool>("--gpu")
        {
            Description = "Draw the picture on the GPU, and fail where there is none rather than use the processor.",
        };

        var processor = new Option<bool>("--processor")
        {
            Description = "Draw the picture on the processor: slower, and the interpreter's bytes exactly. "
                + "Left out, the GPU draws it where there is one.",
        };

        var oversample = new Option<int>("--oversample")
        {
            Description = "Evaluate the sound at this many times the output rate before filtering it down: "
                + string.Join(", ", AudioRenderer.Oversamples) + ". Left out, the editor's Settings → Sound.",
            DefaultValueFactory = _ => defaults.Oversample,
        };

        oversample.AcceptOnlyFromAmong([.. AudioRenderer.Oversamples.Select(factor => factor.ToString(CultureInfo.InvariantCulture))]);

        var input = new Option<FileInfo?>("--input")
        {
            HelpName = "file",
            Description = "A sound file for a Line In to hear, from its start, in place of the microphone a render has none of. "
                + "Left out, a Line In is silent.",
        };

        var mute = new Option<string[]>("--mute")
        {
            HelpName = "group",
            Description = "Switch a group's modules off for the run, by its name (ADR-0117); give it again for more.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
        };

        var solo = new Option<string[]>("--solo")
        {
            HelpName = "group",
            Description = "Hear a group alone: everything is switched off except what feeds it and what carries it to the Output, "
                + "so it keeps its own echo and room. Give it again for more.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
        };

        var command = new Command(
            "render",
            "Write a patch to a picture, a sound, or a clip of both. The size, rate, quality, format "
            + "and ffmpeg left out are the editor's: its preview size and Settings → Recording.")
        {
            patch, preset, presets, output, size, at, seconds, from, fps, quality, format, ffmpeg, loudness, interpreted, gpu,
            processor, settings, oversample, input, mute, solo,
        };

        command.SetAction((result, cancellation) =>
        {
            var error = result.InvocationConfiguration.Error;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, result.InvocationConfiguration.Output);

                return Task.FromResult(Exit.Ok);
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to render: a patch, or --preset and its name.");

                return Task.FromResult(Exit.Failed);
            }

            if (result.GetValue(gpu) && result.GetValue(processor))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --gpu and --processor ask for two different things; say one.");

                return Task.FromResult(Exit.Failed);
            }

            if (result.GetValue(output) is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --out says where to write it.");

                return Task.FromResult(Exit.Failed);
            }

            // A file named relatively is measured from wherever the patch is, so
            // a patch and the sounds and pictures beside it travel together — and
            // a bundle carries them, so one of those needs nothing beside it at
            // all. A preset carries its own files the same way a bundle does.
            Opened opened;

            if (file is not null)
            {
                if (Patches.Open(file, error) is not { } fromFile) return Task.FromResult(Exit.Failed);

                opened = fromFile;
            }
            else
            {
                if (ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped) return Task.FromResult(Exit.Failed);

                opened = shipped.Opened;
            }

            var (loaded, samples, pictures) = opened;

            if (!GroupSwitches.Apply(loaded, result.GetValue(mute) ?? [], result.GetValue(solo) ?? [], error))
                return Task.FromResult(Exit.Failed);

            var (width, height) = result.GetValue(size);
            var into = result.GetRequiredValue(output);

            var options = new RenderOptions(
                into,
                width,
                height,
                result.GetValue(at),
                result.GetValue(seconds),
                result.GetValue(fps),
                result.GetValue(quality),
                result.GetValue(format) ?? defaults.FormatFor(into.Name),
                result.GetValue(ffmpeg) ?? (defaults.FfmpegPath.Length > 0 ? defaults.FfmpegPath : null),
                result.GetValue(loudness),
                result.GetValue(interpreted),
                result.GetValue(gpu) ? PictureBackend.Gpu
                : result.GetValue(processor) ? PictureBackend.Processor
                : PictureBackend.Any,
                result.GetValue(oversample),
                result.GetValue(input),
                result.GetValue(from));

            return Task.FromResult(
                Run(
                    loaded,
                    options,
                    result.InvocationConfiguration.Error,
                    ConsoleProgress.For(),
                    samples,
                    pictures,
                    cancellation: cancellation));
        });

        return command;
    }

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

        if (Refuse(options, still) is { } wrong)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {wrong}");
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

        var video = wantsPicture ? patch.CompileForVideo(samples: samples, pictures: pictures) : null;

        var audio = WantsSound(still, format?.HasPicture ?? true, patch.Reaches().Sound, video?.Program)
            ? patch.CompileForAudio(samples: samples)
            : null;

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

    /// <summary>
    /// Whether a render plays the sound: to write it, or because the picture is of what
    /// the speakers played, a Scope's, a Beam's or a Meter's, in a still and a clip alike.
    /// </summary>
    internal static bool WantsSound(bool still, bool hasPicture, bool reachesSound, CompiledPatch? picture) =>
        (!still && (!hasPicture || reachesSound)) || (picture is not null && Listens(picture));

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

    /// <summary>What is wrong with the numbers a render was given, or null.</summary>
    /// <remarks>Each is multiplied by a rate into a count of samples or frames, so each has a ceiling.</remarks>
    private static string? Refuse(RenderOptions options, bool still)
    {
        const double most = RenderOptions.MostSeconds;

        if (!(options.From >= 0d && options.From <= most)) return $"--from is a second of the patch, from 0 to {most:0}.";
        if (still && !(options.At >= 0d && options.At <= most)) return $"--at is a second of the patch, from 0 to {most:0}.";
        if (!still && !(options.Seconds > 0d && options.Seconds <= most)) return $"--seconds runs above 0 and up to {most:0}.";
        if (!still && !(options.Fps > 0d && options.Fps <= RenderOptions.MostFps)) return $"--fps runs above 0 and up to {RenderOptions.MostFps:0}.";

        return null;
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
