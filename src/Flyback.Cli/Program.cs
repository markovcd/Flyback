using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Parsing;
using System.Text;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>
/// The second shell over the engine. Everything here is argument parsing and where to
/// write the answer; the work is Core's, exactly as it is for the window.
/// </summary>
/// <remarks>
/// A separate program rather than a mode of the shell, for what it does not carry: no
/// Avalonia, so no X libraries, no fonts and no display on the machine that runs it.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        // The sentences this prints are the engine's own, em-dashes and all, and
        // a Windows console left on its system codepage turns those into
        // something else. Attempted rather than assumed: there is not always a
        // console to have an encoding.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console. Whatever is reading this can have the default.
        }

        // The player is a program of its own, so it is handed the rest of the line
        // before there is anything to load or parse: its --help is its own.
        if (ViewerCommand.Claims(args)) return ViewerCommand.Run(args[1..], Console.Error);

        // Before anything reads a patch: a file may name modules that only a
        // plugin defines, and a catalogue settled after the fact would have let
        // it compile against the wrong one.
        var plugins = PluginHost.Load();

        NodeCatalog.Install(plugins.Modules);

        var patch = new Argument<FileInfo>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}.",
        };
        var json = new Option<bool>("--json") { Description = "Write the answer as JSON instead of prose." };

        var root = new RootCommand($"{GlobalConstants.ApplicationName} — a patchable synthesiser, from the command line.")
        {
            Render(patch),
            Check(patch, json),
            Info(patch, json),
            Print(patch),
            Pack(patch, json),
            PackPlugin(),
            PluginKey(),
            Modules(json),
            Probe(plugins, json),
            ViewerCommand.Build(),
            RenderPresetsCommand.Build(),
        };

        // What dotnet-suggest asks for completions with, and the only reason the
        // shell can finish a command this program has.
        var suggest = new SuggestDirective();

        root.Add(suggest);

        var parsed = root.Parse(args);
        var code = parsed.Invoke();

        // Invoked either way, because that is what prints the complaint and the
        // help beneath it. But an argument nobody could parse is the shell being
        // held wrong rather than a patch being wrong, and the two should not
        // come back as the same number. Half-typed input is neither: it is what
        // a completion is asked about.
        return parsed.Errors.Count > 0 && parsed.GetResult(suggest) is null ? Exit.Failed : code;
    }

    /// <summary>Lists the installed catalogue, which is what a plugin adds to.</summary>
    private static Command Modules(Option<bool> json)
    {
        var command = new Command("modules", "Say what modules this build has.")
        {
            json,
        };

        command.SetAction(result => ModulesCommand.Run(
            NodeCatalog.Current, result.GetValue(json), Console.Out));

        return command;
    }

    /// <summary>
    /// A command about an assistant rather than about a patch, which is why it
    /// needs the plugin catalogue rather than the engine: what it asks and what
    /// it writes both belong to a plugin.
    /// </summary>
    private static Command Probe(PluginCatalog plugins, Option<bool> json)
    {
        var provider = new Option<string>("--provider")
        {
            Description = "Which assistant to ask, by id, or `all` for every one with a key. "
                + "Defaults to whichever the settings are on.",
        };

        var model = new Option<string[]>("--model")
        {
            Description = "Ask about these models by name, whether or not the endpoint lists them.",
            AllowMultipleArgumentsPerToken = true,
        };

        var all = new Option<bool>("--all")
        {
            Description = "Ask about everything listed, not only what could build a patch.",
        };

        var bounds = new Option<bool>("--bounds")
        {
            Description = "Also measure what each model will think for. Slow, and billed as thinking.",
        };

        var dry = new Option<bool>("--dry-run")
        {
            Description = "Print what was found and leave the settings as they are.",
        };

        var keys = new Option<bool>("--keys")
        {
            Description = "Say where each provider's key would come from, and ask nothing of anybody.",
        };

        var yes = new Option<bool>("--yes", "-y")
        {
            Description = "Start without the question. A probe is billed traffic, so it is asked for "
                + "first unless this says not to.",
        };

        var command = new Command(
            "probe",
            "Ask an assistant's endpoint which models it has and what each one accepts.")
        {
            provider, model, all, bounds, dry, keys, yes, json,
        };

        command.SetAction((result, cancellation) => ProbeCommand.Run(
            plugins,
            new ProbeOptions(
                result.GetValue(provider),
                result.GetValue(model) ?? [],
                result.GetValue(all),
                result.GetValue(bounds),
                result.GetValue(dry),
                result.GetValue(json),
                result.GetValue(keys),
                result.GetValue(yes)),
            Console.Out,
            Console.Error,
            cancellation,
            asking: Console.IsInputRedirected ? null : Console.In));

        return command;
    }

    private static Command Render(Argument<FileInfo> patch)
    {
        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = "Where to write it. The extension picks the format: .png for a still, "
                + string.Join(", ", ClipFormats.All.Select(f => f.Extension).Distinct())
                + " for the rest.",
            Required = true,
        };

        var size = new Option<(int Width, int Height)>("--size")
        {
            Description = "Frame size, as WIDTHxHEIGHT.",
            DefaultValueFactory = _ => (1920, 1080),
            CustomParser = Size,
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

        var fps = new Option<double>("--fps")
        {
            Description = "Frames a second, for a clip.",
            DefaultValueFactory = _ => MovieRenderer.DefaultFrameRate,
        };

        var quality = new Option<int>("--quality")
        {
            Description = "How good the picture is, 1 to 100 — a JPEG quality in an AVI, "
                + "and a rate factor everywhere else.",
            DefaultValueFactory = _ => JpegWriter.DefaultQuality,
        };

        var format = new Option<string>("--format")
        {
            Description = "Write this format rather than the one the extension names: "
                + string.Join(", ", ClipFormats.All.Select(f => f.Id)) + ".",
        };

        format.CompletionSources.Add(_ => ClipFormats.All.Select(f => new CompletionItem(f.Id, f.Label)));

        var ffmpeg = new Option<string>("--ffmpeg")
        {
            Description = $"The ffmpeg to encode with. Left out, the first on PATH is used, and "
                + $"only {ClipFormats.MotionJpegAvi.Id} and {ClipFormats.Wav.Id} need none at all.",
        };

        var loudness = new Option<bool>("--loudness")
        {
            Description = "Say how loud the sound came out: integrated loudness in LUFS and true peak "
                + "in dBTP, measured as ITU-R BS.1770 does.",
        };

        var command = new Command("render", "Write a patch to a picture, a sound, or a clip of both.")
        {
            patch, output, size, at, seconds, fps, quality, format, ffmpeg, loudness,
        };

        command.SetAction((result, cancellation) =>
        {
            var file = result.GetRequiredValue(patch);

            // A file named relatively is measured from wherever the patch is, so
            // a patch and the sounds and pictures beside it travel together — and
            // a bundle carries them, so one of those needs nothing beside it at
            // all. Which of the two this is, is settled here and nowhere else.
            if (Patches.Open(file, Console.Error) is not { } opened)
                return Task.FromResult(Exit.Failed);

            var (loaded, samples, pictures) = opened;

            var (width, height) = result.GetValue(size);

            var options = new RenderOptions(
                result.GetRequiredValue(output),
                width,
                height,
                result.GetValue(at),
                result.GetValue(seconds),
                result.GetValue(fps),
                result.GetValue(quality),
                result.GetValue(format),
                result.GetValue(ffmpeg),
                result.GetValue(loudness));

            return Task.FromResult(
                RenderCommand.Run(
                    loaded, options, Console.Error, Progress(), samples, pictures, cancellation: cancellation));
        });

        return command;
    }

    /// <summary>
    /// Prints a patch as text. Not built on <see cref="Run"/> either, and for
    /// the opposite reason to <see cref="Pack"/>: what it writes is the patch
    /// rather than a report about one, so there is nothing for a <c>--json</c>
    /// to be an alternative to.
    /// </summary>
    private static Command Print(Argument<FileInfo> patch)
    {
        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Where to write it, .{PatchLanguage.FileExtension} by convention. "
                + "Left out, it goes to standard output.",
        };

        var check = new Option<bool>("--check")
        {
            Description = "Write nothing, and say whether the printing builds back to the same program.",
        };

        var command = new Command("print", "Write a patch out as text, in the language.")
        {
            patch, output, check,
        };

        command.SetAction(result =>
        {
            var checking = result.GetValue(check);
            var into = result.GetValue(output);

            // Said rather than ignored, because the two asked for together are
            // somebody expecting a file at the end of it.
            if (checking && into is not null)
            {
                Console.Error.WriteLine(
                    $"{GlobalConstants.ApplicationName}: --check writes nothing, so there is nothing for --out to take.");

                return Exit.Failed;
            }

            var file = result.GetRequiredValue(patch);

            // Opened rather than read, so that --check compiles a bundle against
            // the files it carries: a program that loaded a table is a different
            // program from one that could not find it, and comparing the second
            // against itself would prove nothing about the first.
            return Patches.Open(file, Console.Error) is not { } opened
                ? Exit.Failed
                : PrintCommand.Run(
                    opened.Patch,
                    file,
                    into,
                    checking,
                    Console.Out,
                    Console.Error,
                    opened.Samples,
                    opened.Pictures);
        });

        return command;
    }

    /// <summary>
    /// Packs a patch and its files into a bundle. Not built on
    /// <see cref="Run"/> like the two below it: those answer questions about a
    /// patch and this writes a file, so it takes an output path rather than a
    /// <c>--json</c>.
    /// </summary>
    private static Command Pack(Argument<FileInfo> patch, Option<bool> json)
    {
        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Where to write the bundle. {PatchBundle.Extension} by convention.",
            Required = true,
        };

        var command = new Command(
            "pack",
            "Put a patch and every file it names into one bundle.")
        {
            patch, output, json,
        };

        command.SetAction(result => PackCommand.Run(
            result.GetRequiredValue(patch),
            result.GetRequiredValue(output),
            Console.Error,
            Console.Out,
            result.GetValue(json)));

        return command;
    }

    /// <summary>Makes a plugin package, the file the editor installs a plugin from.</summary>
    private static Command PackPlugin()
    {
        var source = new Argument<FileSystemInfo>("source")
        {
            Description = "The plugin's project, published here for each runtime it names, "
                + "or the folder the SDK already built it into, such as bin/Release/net10.0.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Where to write the package. {PluginPackage.Extension} by convention.",
            Required = true,
        };

        var key = new Option<FileInfo>("--key", "-k")
        {
            Description = "The private key to sign it with, as plugin-key writes it. "
                + "An update installs only when signed with the key that signed what it replaces.",
        };

        var command = new Command(
            "pack-plugin",
            "Build a plugin and pack it into one signed file the editor installs from.")
        {
            source, output, key,
        };

        command.SetAction(result => PackPluginCommand.Run(
            result.GetRequiredValue(source),
            result.GetRequiredValue(output),
            Console.Out,
            Console.Error,
            key: result.GetValue(key)));

        return command;
    }

    /// <summary>Makes the key a plugin's packages are signed with.</summary>
    private static Command PluginKey()
    {
        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = "Where to write the private key. Keep it, and keep it to yourself.",
            Required = true,
        };

        var command = new Command(
            "plugin-key",
            "Make the key that signs a plugin's packages, and that every update must be signed with.")
        {
            output,
        };

        command.SetAction(result => PluginKeyCommand.Run(result.GetRequiredValue(output), Console.Out, Console.Error));

        return command;
    }

    private static Command Check(Argument<FileInfo> patch, Option<bool> json)
    {
        var strict = new Option<bool>("--strict")
        {
            Description = "Fail on warnings as well as on errors.",
        };

        var command = new Command("check", "Compile a patch and report what is wrong with it.")
        {
            patch, json, strict,
        };

        command.SetAction(result =>
        {
            var file = result.GetRequiredValue(patch);

            return Patches.Open(file, Console.Error) is not { } opened
                ? Exit.Failed
                : CheckCommand.Run(
                    opened.Patch,
                    file.Name,
                    result.GetValue(json),
                    Console.Out,
                    Console.Error,
                    opened.Samples,
                    opened.Pictures,
                    result.GetValue(strict));
        });

        return command;
    }

    private static Command Info(Argument<FileInfo> patch, Option<bool> json)
    {
        var command = new Command("info", "Say what a patch is made of and what each half of it costs.")
        {
            patch, json,
        };

        command.SetAction(result =>
        {
            var file = result.GetRequiredValue(patch);

            return Patches.Open(file, Console.Error) is not { } opened
                ? Exit.Failed
                : InfoCommand.Run(
                    opened.Patch,
                    file.Name,
                    result.GetValue(json),
                    Console.Out,
                    Console.Error,
                    opened.Samples,
                    opened.Pictures);
        });

        return command;
    }

    /// <summary>
    /// Progress for a long render, and only where somebody is watching. Written
    /// to stderr so that it never lands in a redirected file, and carriage
    /// returned so that it is one line rather than a thousand.
    /// </summary>
    private static IProgress<double>? Progress()
    {
        if (Console.IsErrorRedirected) return null;

        var last = -1;

        return new Progress<double>(done =>
        {
            var percent = (int)(done * 100);
            if (percent == last) return;

            last = percent;
            Console.Error.Write($"\rrendering… {percent,3}%");

            if (percent >= 100) Console.Error.WriteLine();
        });
    }

    /// <summary>WIDTHxHEIGHT, and a complaint in the shell's own words when it is not.</summary>
    private static (int Width, int Height) Size(ArgumentResult result)
    {
        var text = result.Tokens[0].Value;

        if (FrameSize.Of(text) is { } size) return size;

        result.AddError(FrameSize.Refuse(text));
        return (0, 0);
    }
}
