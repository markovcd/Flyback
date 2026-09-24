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

        // The viewer is a program of its own, so it is handed the rest of the line
        // before there is anything to load or parse: its --help is its own.
        if (ViewerCommand.Claims(args)) return ViewerCommand.Run(args[1..], Console.Error);

        var plugins = new Plugins(
            PluginHost.Load,
            PluginHost.DefaultDirectory,

            // The report only where somebody is watching, the way the editor keeps it to a
            // terminal it inherited: a script reading stderr wants the command's complaint
            // and nothing else.
            Console.IsErrorRedirected ? null : Console.Error);

        return Run(args, plugins, new InvocationConfiguration());
    }

    /// <summary>The commands, and what the shell is told the chosen one did.</summary>
    internal static int Run(string[] args, Plugins plugins, InvocationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var patch = new Argument<FileInfo>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}.",
        };
        var json = new Option<bool>("--json") { Description = "Write the answer as JSON instead of prose." };

        var exports = ExportDefaults.Load(ExportDefaults.PathIn(args) ?? ExportDefaults.File);

        var root = new RootCommand($"{GlobalConstants.ApplicationName} — a patchable synthesiser, from the command line.")
        {
            Render(plugins, patch, exports),
            Check(plugins, patch, json),
            Info(plugins, patch, json),
            Print(plugins, patch),
            Pack(plugins, patch, json),
            PackPlugin(),
            PluginKey(),
            Modules(plugins, json),
            Compare(plugins, json),
            Probe(plugins, json),
            ViewerCommand.Build(),
            RenderPresetsCommand.Build(plugins),
        };

        // What dotnet-suggest asks for completions with, and the only reason the
        // shell can finish a command this program has.
        var suggest = new SuggestDirective();

        root.Add(suggest);

        var parsed = root.Parse(args);
        var code = parsed.Invoke(configuration);

        // Invoked either way, because that is what prints the complaint and the
        // help beneath it. But an argument nobody could parse is the shell being
        // held wrong rather than a patch being wrong, and the two should not
        // come back as the same number. Half-typed input is neither: it is what
        // a completion is asked about.
        return parsed.Errors.Count > 0 && parsed.GetResult(suggest) is null ? Exit.Failed : code;
    }

    /// <summary>Plays two patches side by side and says whether they are the same instrument.</summary>
    private static Command Compare(Plugins plugins, Option<bool> json)
    {
        var was = new Argument<FileInfo>("was") { Description = "The patch as it was." };
        var now = new Argument<FileInfo>("now") { Description = "The patch as it is now." };

        var seconds = new Option<double>("--seconds")
        {
            Description = "How long to play both.",
            DefaultValueFactory = _ => 10d,
        };

        var size = new Option<(int Width, int Height)>("--size")
        {
            Description = "The frame both are drawn at, as WIDTHxHEIGHT.",
            DefaultValueFactory = _ => (320, 180),
            CustomParser = Size,
        };

        var command = new Command(
            "compare",
            "Play two patches side by side and say whether they are the same instrument, bit for bit.")
        {
            was, now, seconds, size, json,
        };

        command.SetAction((result, cancellation) =>
        {
            plugins.Ready();

            var error = result.InvocationConfiguration.Error;
            var first = result.GetRequiredValue(was);
            var second = result.GetRequiredValue(now);

            if (Patches.Open(first, error) is not { } before || Patches.Open(second, error) is not { } after)
                return Task.FromResult(Exit.Failed);

            var (width, height) = result.GetValue(size);

            return Task.FromResult(CompareCommand.Run(
                before,
                first.Name,
                after,
                second.Name,
                new CompareOptions(result.GetValue(seconds), width, height, Json: result.GetValue(json)),
                result.InvocationConfiguration.Output,
                error,
                cancellation));
        });

        return command;
    }

    /// <summary>Lists the installed catalog, which is what a plugin adds to, or describes one module in it.</summary>
    private static Command Modules(Plugins plugins, Option<bool> json)
    {
        var module = new Argument<string?>("module")
        {
            Description = "One module to describe, by type id or name: its sockets, defaults, ranges and what it does.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var command = new Command("modules", "Say what modules this build has, or everything about one of them.")
        {
            module, json,
        };

        command.SetAction(result =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;

            return result.GetValue(module) is { } wanted
                ? ModulesCommand.Describe(
                    NodeCatalog.Current, wanted, result.GetValue(json), output, result.InvocationConfiguration.Error)
                : ModulesCommand.Run(NodeCatalog.Current, result.GetValue(json), output);
        });

        return command;
    }

    /// <summary>
    /// A command about an assistant rather than about a patch, which is why it
    /// needs the plugin catalog rather than the engine: what it asks and what
    /// it writes both belong to a plugin.
    /// </summary>
    private static Command Probe(Plugins plugins, Option<bool> json)
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
            plugins.Catalog,
            new ProbeOptions(
                result.GetValue(provider),
                result.GetValue(model) ?? [],
                result.GetValue(all),
                result.GetValue(bounds),
                result.GetValue(dry),
                result.GetValue(json),
                result.GetValue(keys),
                result.GetValue(yes)),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancellation,
            asking: Console.IsInputRedirected ? null : Console.In));

        return command;
    }

    private static Command Render(Plugins plugins, Argument<FileInfo> patch, ExportDefaults defaults)
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
            DefaultValueFactory = _ => (defaults.Width, defaults.Height),
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
            DefaultValueFactory = _ => defaults.Fps,
        };

        var quality = new Option<int>("--quality")
        {
            Description = "How good the picture is, 1 to 100 — a JPEG quality in an AVI, "
                + "and a rate factor everywhere else.",
            DefaultValueFactory = _ => defaults.Quality,
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
            Description = "Read the defaults from another output.json than the editor's.",
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

        var command = new Command(
            "render",
            "Write a patch to a picture, a sound, or a clip of both. The size, rate, quality, format "
            + "and ffmpeg left out are the editor's: its preview size and Settings → Recording.")
        {
            patch, output, size, at, seconds, fps, quality, format, ffmpeg, loudness, interpreted, settings,
        };

        command.SetAction((result, cancellation) =>
        {
            plugins.Ready();

            var file = result.GetRequiredValue(patch);

            // A file named relatively is measured from wherever the patch is, so
            // a patch and the sounds and pictures beside it travel together — and
            // a bundle carries them, so one of those needs nothing beside it at
            // all. Which of the two this is, is settled here and nowhere else.
            if (Patches.Open(file, result.InvocationConfiguration.Error) is not { } opened)
                return Task.FromResult(Exit.Failed);

            var (loaded, samples, pictures) = opened;

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
                result.GetValue(ffmpeg) ?? defaults.Ffmpeg,
                result.GetValue(loudness),
                result.GetValue(interpreted));

            return Task.FromResult(
                RenderCommand.Run(
                    loaded,
                    options,
                    result.InvocationConfiguration.Error,
                    Progress(),
                    samples,
                    pictures,
                    cancellation: cancellation));
        });

        return command;
    }

    /// <summary>
    /// Prints a patch as text. No <c>--json</c>: what it writes is the patch rather
    /// than a report about one, so there is nothing for a <c>--json</c> to be an
    /// alternative to.
    /// </summary>
    private static Command Print(Plugins plugins, Argument<FileInfo> patch)
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
                result.InvocationConfiguration.Error.WriteLine(
                    $"{GlobalConstants.ApplicationName}: --check writes nothing, so there is nothing for --out to take.");

                return Exit.Failed;
            }

            plugins.Ready();

            var file = result.GetRequiredValue(patch);

            // Opened rather than read, so that --check compiles a bundle against
            // the files it carries: a program that loaded a table is a different
            // program from one that could not find it, and comparing the second
            // against itself would prove nothing about the first.
            return Patches.Open(file, result.InvocationConfiguration.Error) is not { } opened
                ? Exit.Failed
                : PrintCommand.Run(
                    opened.Patch,
                    file,
                    into,
                    checking,
                    result.InvocationConfiguration.Output,
                    result.InvocationConfiguration.Error,
                    opened.Samples,
                    opened.Pictures);
        });

        return command;
    }

    /// <summary>
    /// Packs a patch and its files into a bundle. It writes a file rather than only
    /// answering for one, so it takes an output path as well as the <c>--json</c> the
    /// reports below take.
    /// </summary>
    private static Command Pack(Plugins plugins, Argument<FileInfo> patch, Option<bool> json)
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

        command.SetAction(result =>
        {
            plugins.Ready();

            return PackCommand.Run(
                result.GetRequiredValue(patch),
                result.GetRequiredValue(output),
                result.InvocationConfiguration.Error,
                result.InvocationConfiguration.Output,
                result.GetValue(json));
        });

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
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
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

        command.SetAction(result => PluginKeyCommand.Run(
            result.GetRequiredValue(output),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        return command;
    }

    private static Command Check(Plugins plugins, Argument<FileInfo> patch, Option<bool> json)
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
            plugins.Ready();

            var file = result.GetRequiredValue(patch);

            // Text that does not read is a patch with something wrong with it, not
            // a file that could not be looked at, so it answers the way a compile
            // error does.
            if (Patches.Sourced(file) && file.Exists
                && PatchLanguage.Build(File.ReadAllText(file.FullName)) is { Ok: false } unread)
            {
                return CheckCommand.Unread(
                    unread.Issues,
                    file.Name,
                    result.GetValue(json),
                    result.InvocationConfiguration.Output,
                    result.InvocationConfiguration.Error);
            }

            return Patches.Open(file, result.InvocationConfiguration.Error) is not { } opened
                ? Exit.Failed
                : CheckCommand.Run(
                    opened.Patch,
                    file.Name,
                    result.GetValue(json),
                    result.InvocationConfiguration.Output,
                    result.InvocationConfiguration.Error,
                    opened.Samples,
                    opened.Pictures,
                    result.GetValue(strict));
        });

        return command;
    }

    private static Command Info(Plugins plugins, Argument<FileInfo> patch, Option<bool> json)
    {
        var command = new Command("info", "Say what a patch is made of and what each half of it costs.")
        {
            patch, json,
        };

        command.SetAction(result =>
        {
            plugins.Ready();

            var file = result.GetRequiredValue(patch);

            return Patches.Open(file, result.InvocationConfiguration.Error) is not { } opened
                ? Exit.Failed
                : InfoCommand.Run(
                    opened.Patch,
                    file.Name,
                    result.GetValue(json),
                    result.InvocationConfiguration.Output,
                    result.InvocationConfiguration.Error,
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
