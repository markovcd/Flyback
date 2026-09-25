using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Avalonia;
using Flyback.App;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;

namespace Flyback.Viewer;

/// <summary>What the program tells the shell it did.</summary>
internal static class Exit
{
    public const int Ok = 0;

    /// <summary>There was nothing to play: no such file, a patch that does not read, a name no preset has.</summary>
    public const int Failed = 2;
}

/// <summary>
/// The command line, with the settings file behind every default it has.
/// </summary>
/// <remarks>
/// Built from the settings rather than beside them, so <c>--help</c> prints what
/// this machine is set to and not what a clean install would be. The action is
/// handed the settled <see cref="ViewerOptions"/> and does everything else; this
/// only says what the flags mean and refuses the ones that contradict each other.
/// </remarks>
internal static class ViewerArguments
{
    public static RootCommand Build(OutputSettings settings, Func<ViewerOptions, int> run, TextWriter error)
    {
        var patch = new Argument<string>("patch")
        {
            Description = "A document, a bundle, or a patch written as text "
                + $"(.{PatchIO.FileExtension}, {PatchBundle.Extension}, .{PatchLanguage.FileExtension}). "
                + "Left out, the startup preset from the settings plays.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset")
        {
            HelpName = "name",
            Description = "A shipped preset or one saved in the gallery, by name.",
        };

        var presets = new Option<bool>("--presets")
        {
            Description = "List what --preset would accept, and stop.",
        };

        var size = new Option<string>("--size")
        {
            HelpName = "WxH|name",
            Description = "What the picture is drawn at: WIDTHxHEIGHT, or " + string.Join(", ", Resolutions.Names.Keys) + ".",
            DefaultValueFactory = _ => $"{settings.Width}x{settings.Height}",
        };

        size.Validators.Add(result =>
        {
            var text = result.GetValueOrDefault<string>();

            if (text is not null && Sized(text) is null) result.AddError(FrameSize.Refuse(text));
        });

        var fps = new Option<double>("--fps")
        {
            HelpName = "n",
            Description = "Preview frames a second; 0 draws as fast as the renderer allows.",
            DefaultValueFactory = _ => settings.PreviewFrameRate,
        };

        Atleast(fps, 0);

        var gpu = new Option<bool>("--gpu")
        {
            Description = "Draw the picture on the graphics card" + (settings.Gpu ? " (the settings' choice)." : "."),
        };

        var cpu = new Option<bool>("--cpu")
        {
            Description = "Draw the picture on the processor" + (settings.Gpu ? "." : " (the settings' choice)."),
        };

        var noVideo = new Option<bool>("--no-video")
        {
            Description = "Compile no picture and draw none; the window is just its buttons.",
        };

        var window = new Option<string>("--window")
        {
            HelpName = "WxH",
            Description = "The window's own size, apart from what the patch is drawn at: WIDTHxHEIGHT.",
        };

        window.Validators.Add(result =>
        {
            var text = result.GetValueOrDefault<string>();

            if (text is not null && Sized(text) is null) result.AddError(FrameSize.Refuse(text));
        });

        var maximized = new Option<bool>("--maximized") { Description = "Open maximized." };
        var fullScreen = new Option<bool>("--full-screen") { Description = "Open full screen; refused for a patch with no picture." };

        var noAudio = new Option<bool>("--no-audio") { Description = "Open no sound device at all." };

        var volume = new Option<float>("--volume")
        {
            HelpName = "0..1",
            Description = "How loud, from 0 to 1, against what the patch made.",
            DefaultValueFactory = _ => 1f,
        };

        volume.Validators.Add(result =>
        {
            if (Typed<float>(result) is { } value && value is not (>= 0f and <= 1f)) result.AddError("--volume is a level from 0 to 1.");
        });

        var mute = new Option<bool>("--mute")
        {
            Description = "Start turned down; the speaker button brings it back.",
        };

        var latency = new Option<int>("--latency")
        {
            HelpName = "ms",
            Description = $"How many milliseconds behind the speakers may run, {OutputSettings.ShortestLatency} to {OutputSettings.LongestLatency}.",
            DefaultValueFactory = _ => settings.LatencyMilliseconds,
        };

        latency.Validators.Add(result =>
        {
            if (Typed<int>(result) is { } value
                && (value < OutputSettings.ShortestLatency || value > OutputSettings.LongestLatency))
                result.AddError($"--latency is {OutputSettings.ShortestLatency} to {OutputSettings.LongestLatency} milliseconds.");
        });

        var from = new Option<double>("--from")
        {
            HelpName = "seconds",
            Description = "Start there, in seconds, rather than at nought.",
        };

        Atleast(from, 0);

        var paused = new Option<bool>("--paused") { Description = "Open on the first frame, stopped." };

        var duration = new Option<double>("--for")
        {
            HelpName = "seconds",
            Description = "Play that many seconds, then close.",
        };

        Above(duration, 0);

        var loop = new Option<double>("--loop")
        {
            HelpName = "seconds",
            Description = "Rewind to nought every that many seconds.",
        };

        Above(loop, 0);

        var background = new Option<bool>("--background")
        {
            Description = "Open without taking focus and without coming to the front.",
        };

        var hidden = new Option<bool>("--hidden")
        {
            Description = "Open no window: the patch plays and nothing appears on screen. Implies --no-video.",
        };

        var noOverlay = new Option<bool>("--no-overlay")
        {
            Description = "No dots and no toolbar, so a capture has nothing of the viewer in it.",
        };

        var transport = new Option<string>("--transport")
        {
            HelpName = "top|bottom",
            Description = "Which edge of the picture the transport and the seek bar wait at; the knobs take the other.",
            DefaultValueFactory = _ => settings.Transport == TransportEdge.Bottom ? "bottom" : "top",
        };

        transport.AcceptOnlyFromAmong("top", "bottom");

        var stats = new Option<bool>("--stats")
        {
            Description = "Say in the corner of the picture how it is drawn: frames a second, a frame's cost, the ops. F3 shows it and puts it away.",
        };

        var title = new Option<string>("--title")
        {
            HelpName = "text",
            Description = "What the title bar says.",
        };

        var top = new Option<bool>("--top") { Description = "Keep the window above the others." };

        var interpreted = new Option<bool>("--interpreted")
        {
            Description = "Keep the processor's program on the interpreter rather than compiling it.",
        };

        var file = new Option<string>("--settings")
        {
            HelpName = "path",
            Description = "Read the defaults from another output.json.",
        };

        var root = new RootCommand(
            "Flyback Viewer — open a patch and play it, picture and sound, and write nothing. "
            + "A patch made to be played takes the computer's keys and its MIDI devices; "
            + "Space, or Ctrl+P, pauses it, F11 gives it the whole screen, and F3 says how it is drawn.")
        {
            patch, preset, presets,
            size, fps, gpu, cpu, noVideo, window, maximized, fullScreen,
            noAudio, volume, mute, latency,
            from, paused, duration, loop,
            background, hidden, noOverlay, transport, stats, title, top, interpreted, file,
        };

        root.SetAction(result =>
        {
            var chosen = result.GetValue(patch);
            var named = result.GetValue(preset);

            if (result.GetValue(gpu) && result.GetValue(cpu))
                return Refuse(error, "--gpu and --cpu name different renderers; pick one.");

            if (chosen is not null && named is not null)
                return Refuse(error, "A patch and --preset both say what to play; give one.");

            var settled = new ViewerOptions
            {
                Patch = chosen,
                Preset = named,
                ListPresets = result.GetValue(presets),
                Size = Sized(result.GetValue(size)!) ?? new PixelSize(settings.Width, settings.Height),
                FrameRate = result.GetValue(fps),
                Gpu = result.GetValue(gpu) || (!result.GetValue(cpu) && settings.Gpu),
                NoVideo = result.GetValue(noVideo),
                Window = result.GetResult(window) is not null ? Sized(result.GetValue(window)!) : null,
                Maximized = result.GetValue(maximized),
                FullScreen = result.GetValue(fullScreen),
                NoAudio = result.GetValue(noAudio),
                Volume = result.GetValue(volume),
                Mute = result.GetValue(mute),
                LatencyMilliseconds = result.GetValue(latency),
                From = result.GetValue(from),
                Paused = result.GetValue(paused),
                For = result.GetResult(duration) is not null ? result.GetValue(duration) : null,
                Loop = result.GetResult(loop) is not null ? result.GetValue(loop) : null,
                Background = result.GetValue(background),
                Hidden = result.GetValue(hidden),
                NoOverlay = result.GetValue(noOverlay),
                Transport = result.GetValue(transport) == "bottom" ? TransportEdge.Bottom : TransportEdge.Top,
                Stats = result.GetValue(stats),
                Title = result.GetValue(title),
                Top = result.GetValue(top),
                Interpreted = result.GetValue(interpreted),
            };

            if (settled.Hidden && Windowed(settled) is { } flag)
                return Refuse(error, $"{flag} shapes a window, and --hidden opens none.");

            if (settled.FullScreen && settled.NoVideo)
                return Refuse(error, "--full-screen fills the screen with the picture, and --no-video draws none.");

            return run(settled);
        });

        return root;
    }

    /// <summary>The flag that wants a window, for one that was asked not to have any.</summary>
    private static string? Windowed(ViewerOptions options) =>
        options.FullScreen ? "--full-screen"
        : options.Maximized ? "--maximized"
        : options.Window is not null ? "--window"
        : options.Top ? "--top"
        : options.NoOverlay ? "--no-overlay"
        : options.Stats ? "--stats"
        : null;

    /// <summary>A size as it is typed: a short name or a row's label, or WIDTHxHEIGHT.</summary>
    internal static PixelSize? Sized(string text)
    {
        if (Resolutions.Named(text) is { } named) return named;

        return FrameSize.Of(text) is var (width, height) ? new PixelSize(width, height) : null;
    }

    /// <summary>The <c>--settings</c> a command line names, found before the command exists.</summary>
    /// <remarks>
    /// The defaults are read from that file and the command is built from them, so it
    /// cannot wait for the parse.
    /// </remarks>
    public static string? SettingsPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--settings") return i + 1 < args.Length ? args[i + 1] : null;

            if (args[i].StartsWith("--settings=", StringComparison.Ordinal)) return args[i]["--settings=".Length..];
        }

        return null;
    }

    private static int Refuse(TextWriter error, string sentence)
    {
        error.WriteLine($"{GlobalConstants.ApplicationName}: {sentence}");

        return Exit.Failed;
    }

    /// <summary>
    /// What was typed, as the option's type; null where it does not read as one,
    /// which the parser says for itself.
    /// </summary>
    private static T? Typed<T>(OptionResult result)
        where T : struct
    {
        try
        {
            return result.GetValueOrDefault<T>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void Atleast(Option<double> option, double least) =>
        option.Validators.Add(result =>
        {
            if (Typed<double>(result) is { } value && (!double.IsFinite(value) || value < least))
                result.AddError($"{option.Name} cannot be less than {least.ToString(CultureInfo.InvariantCulture)}.");
        });

    private static void Above(Option<double> option, double least) =>
        option.Validators.Add(result =>
        {
            if (Typed<double>(result) is { } value && (!double.IsFinite(value) || value <= least))
                result.AddError($"{option.Name} has to be more than {least.ToString(CultureInfo.InvariantCulture)}.");
        });
}
