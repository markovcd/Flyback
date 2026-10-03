using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Parsing;
using System.Text;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Measure;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;

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

        var plugins = new PluginRegistry(
            PluginHost.Load,
            PluginHost.DefaultDirectory,

            // The report only where somebody is watching, the way the editor keeps it to a
            // terminal it inherited: a script reading stderr wants the command's complaint
            // and nothing else.
            Console.IsErrorRedirected ? null : Console.Error);

        return Run(args, plugins, new InvocationConfiguration());
    }

    /// <summary>The commands, and what the shell is told the chosen one did.</summary>
    internal static int Run(string[] args, PluginRegistry plugins, InvocationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var json = new Option<bool>("--json") { Description = "Write the answer as JSON instead of prose." };

        var exports = ExportDefaults.Load(ExportDefaults.PathIn(args) ?? SettingsFile.Path);

        var root = new RootCommand($"{GlobalConstants.ApplicationName} — a patchable synthesiser, from the command line.")
        {
            Render(plugins, exports),
            Check(plugins, json),
            Info(plugins, json),
            Print(plugins),
            Pack(plugins, json),
            Save(plugins),
            PackPlugin(),
            PluginKey(),
            Plugin(plugins, json),
            Modules(plugins, json),
            Compare(plugins, json),
            Probe(plugins, json),
            Measure(plugins, json),
            Ask(plugins, json),
            ViewerCommand.Build(),
            ShotCommand.Build(plugins),
            RenderPresetsCommand.Build(plugins),
            StillsCommand.Build(plugins),
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
    private static Command Compare(PluginRegistry plugins, Option<bool> json)
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
    private static Command Modules(PluginRegistry plugins, Option<bool> json)
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
    private static Command Probe(PluginRegistry plugins, Option<bool> json)
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

    /// <summary>Asks the assistant the editor is set to about a patch, and writes its answer back.</summary>
    private static Command Ask(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to talk about, written back with each answer: "
                + $"{AskedPatch.Formats}. One that does not exist yet starts empty.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var message = new Argument<string[]>("message")
        {
            Description = "What to ask. Left out, it is read from standard input, "
                + "or asked for line by line at a terminal.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "Start from a shipped preset, by name, in place of a file. Needs --out.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Write the answers here rather than over the patch: {AskedPatch.Formats}.",
        };

        var provider = new Option<string>("--provider")
        {
            Description = "Which assistant, by id. Defaults to whichever the settings are on.",
        };

        var model = new Option<string>("--model")
        {
            Description = $"The model, for this run only. Short for --set {AssistantSchema.ModelKey}=NAME.",
        };

        var set = new Option<string[]>("--set")
        {
            Description = "A provider setting for this run only, as key=value. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };

        var fresh = new Option<bool>("--fresh")
        {
            Description = "Start a new conversation rather than carry on the one saved with the patch.",
        };

        var seen = new Option<DirectoryInfo>("--seen")
        {
            Description = "Write each picture it looks at and sound it hears into this folder.",
        };

        var briefing = new Option<bool>("--briefing")
        {
            Description = "Print the briefing the assistant is handed when a conversation starts.",
        };

        var turns = new Option<int?>("--turns")
        {
            Description = $"How many turns the conversation may have, {AssistantSettings.FewestTurns} to "
                + $"{AssistantSettings.MostTurns}. Defaults to the editor's Settings → Assistant.",
        };

        var command = new Command(
            "ask",
            "Ask the assistant to change a patch, the way the editor's assistant column does, and write "
            + "the patch back with the conversation, so the next ask, or the editor, carries it on.")
        {
            patch, message, preset, output, provider, model, set, fresh, seen, briefing, turns, json,
        };

        command.Validators.Add(result =>
        {
            if (result.GetValue(turns) is { } limit and (< AssistantSettings.FewestTurns or > AssistantSettings.MostTurns))
                result.AddError($"--turns is {AssistantSettings.FewestTurns} to {AssistantSettings.MostTurns}.");
        });

        command.SetAction((result, cancellation) =>
        {
            plugins.Ready();

            var error = result.InvocationConfiguration.Error;
            var file = result.GetValue(patch);
            var named = result.GetValue(preset);
            var words = result.GetValue(message) ?? [];

            // With --preset there is no patch, so the first word of the message
            // landed where the patch would have.
            if (named is not null && file is not null)
            {
                words = [result.GetResult(patch)!.Tokens[0].Value, .. words];
                file = null;
            }

            if (AskCommand.Open(plugins.Catalog, file, named, result.GetValue(output), error) is not { } about)
                return Task.FromResult(Exit.Failed);

            var settings = (result.GetValue(set) ?? []).ToList();

            if (result.GetValue(model) is { } chosen) settings.Add($"{AssistantSchema.ModelKey}={chosen}");

            return AskCommand.Run(
                plugins.Catalog,
                about,
                new AskOptions(
                    words.Length == 0 ? null : string.Join(' ', words),
                    result.GetValue(provider),
                    settings,
                    result.GetValue(fresh),
                    result.GetValue(json),
                    result.GetValue(seen),
                    result.GetValue(briefing),
                    result.GetValue(turns)),
                result.InvocationConfiguration.Output,
                error,
                Console.In,
                Console.IsInputRedirected ? null : Console.In,
                cancellation);
        });

        return command;
    }

    private static Command Render(PluginRegistry plugins, ExportDefaults defaults)
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

        oversample.AcceptOnlyFromAmong([.. AudioRenderer.Oversamples.Select(factor => factor.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

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
            patch, preset, presets, output, size, at, seconds, fps, quality, format, ffmpeg, loudness, interpreted, gpu,
            processor, settings, oversample, mute, solo,
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
                result.GetValue(ffmpeg) ?? defaults.Ffmpeg,
                result.GetValue(loudness),
                result.GetValue(interpreted),
                result.GetValue(gpu) ? PictureBackend.Gpu
                : result.GetValue(processor) ? PictureBackend.Processor
                : PictureBackend.Any,
                result.GetValue(oversample));

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
    private static Command Print(PluginRegistry plugins)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}.",
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
            Description = $"Where to write it, .{PatchLanguage.FileExtension} by convention. "
                + "Left out, it goes to standard output.",
        };

        var check = new Option<bool>("--check")
        {
            Description = "Write nothing, and say whether the printing builds back to the same program.",
        };

        var command = new Command("print", "Write a patch out as text, in the language.")
        {
            patch, preset, presets, output, check,
        };

        command.SetAction(result =>
        {
            var checking = result.GetValue(check);
            var into = result.GetValue(output);
            var error = result.InvocationConfiguration.Error;

            // Said rather than ignored, because the two asked for together are
            // somebody expecting a file at the end of it.
            if (checking && into is not null)
            {
                result.InvocationConfiguration.Error.WriteLine(
                    $"{GlobalConstants.ApplicationName}: --check writes nothing, so there is nothing for --out to take.");

                return Exit.Failed;
            }

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, result.InvocationConfiguration.Output);

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to print: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            if (file is null)
            {
                if (ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped) return Exit.Failed;

                return PrintCommand.Run(
                    shipped.Opened.Patch,
                    null,
                    into,
                    checking,
                    result.InvocationConfiguration.Output,
                    error,
                    shipped.Opened.Samples,
                    shipped.Opened.Pictures,
                    name: shipped.Name);
            }

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
    private static Command Pack(PluginRegistry plugins, Option<bool> json)
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
            Description = "A shipped preset, by name, in place of a file. The bundle carries the files it ships with.",
        };

        var presets = new Option<bool>("--presets")
        {
            Description = "List what --preset would accept, and stop.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Where to write the bundle. {PatchBundle.Extension} by convention.",
        };

        var command = new Command(
            "pack",
            "Put a patch and every file it names into one bundle.")
        {
            patch, preset, presets, output, json,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;
            var writer = result.InvocationConfiguration.Output;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, writer, result.GetValue(json));

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to pack: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            if (result.GetValue(output) is not { } into)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --out says where to write the bundle.");

                return Exit.Failed;
            }

            if (file is not null) return PackCommand.Run(file, into, error, writer, result.GetValue(json));

            if (ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped) return Exit.Failed;

            var carried = (shipped.Opened.Samples as BundleFiles)?.Bytes;

            return PackCommand.Run(
                shipped.Opened.Patch,
                path => carried?.GetValueOrDefault(path),
                into,
                error,
                writer,
                result.GetValue(json));
        });

        return command;
    }

    /// <summary>
    /// Saves a patch or a shipped preset as a document, a bundle or text, whichever
    /// the output's extension names. It says nothing on success: the file is the answer.
    /// </summary>
    private static Command Save(PluginRegistry plugins)
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
            Description = $"Where to write it. The extension says what to write: {SaveCommand.Formats}.",
        };

        var command = new Command(
            "save",
            "Save a patch or a shipped preset as a patch file, a bundle or text, by the extension it is saved to.")
        {
            patch, preset, presets, output,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;
            var writer = result.InvocationConfiguration.Output;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, writer);

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to save: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            if (result.GetValue(output) is not { } into)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --out says where to save it, {SaveCommand.Formats}.");

                return Exit.Failed;
            }

            if (file is not null) return SaveCommand.Run(file, into, plugins.Catalog.Modules, error, writer);

            if (ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped) return Exit.Failed;

            return SaveCommand.Run(
                shipped.Opened.Patch,
                shipped.Name,
                (shipped.Opened.Samples as BundleFiles)?.Bytes,
                into,
                NodeCatalog.Current,
                error,
                writer);
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

    /// <summary>Which plugin folders load, and saying yes or no to one.</summary>
    private static Command Plugin(PluginRegistry plugins, Option<bool> json)
    {
        var folder = new Argument<string>("folder")
        {
            Description = $"The plugin's folder: a path, or a name under {PluginHost.DirectoryName}/ beside this program.",
        };

        var secrets = new Option<bool>("--secrets")
        {
            Description = "Let it register a secret store, which holds every assistant key typed from then on.",
        };

        var allow = new Command("allow", "Load a plugin folder from the next start, as its files stand now.") { folder, secrets };

        allow.SetAction(result => PluginCommand.Allow(
            PluginCommand.Resolve(result.GetRequiredValue(folder), plugins.Directory),
            result.GetValue(secrets),
            plugins.Trust().Allowances,
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        var denied = new Argument<string>("folder")
        {
            Description = $"The plugin's folder: a path, or a name under {PluginHost.DirectoryName}/ beside this program.",
        };

        var deny = new Command("deny", "Stop loading a plugin folder that was allowed.") { denied };

        deny.SetAction(result => PluginCommand.Deny(
            PluginCommand.Resolve(result.GetRequiredValue(denied), plugins.Directory),
            plugins.Trust().Allowances,
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        var list = new Command("list", "Say which plugin folders load, and why the others do not.") { json };

        list.SetAction(result => PluginCommand.List(
            plugins.Directory,
            plugins.Trust(),
            result.GetValue(json),
            result.InvocationConfiguration.Output));

        var package = new Argument<FileInfo>("package")
        {
            Description = $"The {PluginPackage.Extension} to read.",
        };

        var describe = new Command(
            "describe",
            "Say what a plugin package is and what its code names, as the install dialog reads it, running none of it.")
        {
            package, json,
        };

        describe.SetAction(result => PluginDescribeCommand.Run(
            result.GetRequiredValue(package),
            result.GetValue(json),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        return new Command("plugin", "Which plugin folders load, and what a plugin package holds.")
        {
            allow, deny, list, describe,
        };
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

    /// <summary>Runs a patch offline and says what every output carried.</summary>
    private static Command Measure(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<string>("patch")
        {
            Description = "The patch to measure: a document, a bundle, or one written as text. "
                + "Left out with --preset, whose outputs then follow at once.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var outputs = new Argument<string[]>("outputs")
        {
            Description = "Which outputs: a module for all of its outputs, or module.output for one. "
                + "Two modules with one title are numbered in patch order: 'Oscillator 2'. Left out, every output.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var seconds = new Option<double>("--seconds")
        {
            Description = "How long to run the patch.",
            DefaultValueFactory = _ => MeasureOptions.DefaultSeconds,
        };

        var from = new Option<double>("--from")
        {
            Description = "Where on the patch's clock to start, in seconds. Memory starts empty there.",
        };

        var command = new Command(
            "measure",
            "Run a patch offline and say what every output carries, to the speakers and to the screen: "
            + "its value, or its range and how fast it changes.")
        {
            patch, outputs, preset, seconds, from, json,
        };

        command.SetAction((result, cancellation) =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var first = result.GetValue(patch);
            var named = result.GetValue(outputs) ?? [];
            var shipped = result.GetValue(preset);

            Opened? opened;

            if (shipped is not null)
            {
                // The first word was an output, there being no file to name.
                if (first is not null) named = [first, .. named];

                opened = ShippedPresets.Open(plugins.Catalog, shipped, error)?.Opened;
            }
            else if (first is not null)
            {
                opened = Patches.Open(new FileInfo(first), error);
            }
            else
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to measure: a patch, or --preset and its name.");

                return Task.FromResult(Exit.Failed);
            }

            if (opened is not { } found) return Task.FromResult(Exit.Failed);

            return Task.FromResult(MeasureCommand.Run(
                found.Patch,
                named,
                new MeasureOptions(result.GetValue(seconds), result.GetValue(from)),
                result.GetValue(json),
                NodeCatalog.Current,
                output,
                error,
                found.Samples,
                found.Pictures,
                result.GetValue(json) ? null : Progress("measuring"),
                cancellation));
        });

        return command;
    }

    private static Command Check(PluginRegistry plugins, Option<bool> json)
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

        var strict = new Option<bool>("--strict")
        {
            Description = "Fail on warnings as well as on errors.",
        };

        var command = new Command("check", "Compile a patch and report what is wrong with it.")
        {
            patch, preset, presets, json, strict,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, result.InvocationConfiguration.Output, result.GetValue(json));

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to check: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            if (file is null)
            {
                return ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped
                    ? Exit.Failed
                    : CheckCommand.Run(
                        shipped.Opened.Patch,
                        shipped.Name,
                        result.GetValue(json),
                        result.InvocationConfiguration.Output,
                        error,
                        shipped.Opened.Samples,
                        shipped.Opened.Pictures,
                        result.GetValue(strict));
            }

            var read = Patches.Sourced(file) && file.Exists ? PatchLanguage.Build(File.ReadAllText(file.FullName)) : null;

            // Text that does not read is a patch with something wrong with it, not
            // a file that could not be looked at, so it answers the way a compile
            // error does.
            if (read is { Ok: false } unread)
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
                    result.GetValue(strict),
                    read?.Issues);
        });

        return command;
    }

    private static Command Info(PluginRegistry plugins, Option<bool> json)
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

        var byGroup = new Option<bool>("--by-group")
        {
            Description = "Also list what each group adds to the picture's and the sound's ops, so what makes a patch heavy is one command.",
        };

        var command = new Command("info", "Say what a patch is made of and what each half of it costs.")
        {
            patch, preset, presets, byGroup, json,
        };

        command.SetAction(result =>
        {
            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, output, result.GetValue(json));

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to describe: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            Opened? opened;
            string name;

            if (file is not null)
            {
                opened = Patches.Open(file, error);
                name = file.Name;
            }
            else
            {
                var shipped = ShippedPresets.Open(plugins.Catalog, named!, error);

                opened = shipped?.Opened;
                name = shipped?.Name ?? named!;
            }

            return opened is not { } found
                ? Exit.Failed
                : InfoCommand.Run(found.Patch, name, result.GetValue(json), output, error, found.Samples, found.Pictures, result.GetValue(byGroup));
        });

        return command;
    }

    /// <summary>
    /// Progress for a long render, and only where somebody is watching. Written
    /// to stderr so that it never lands in a redirected file, and carriage
    /// returned so that it is one line rather than a thousand.
    /// </summary>
    private static IProgress<double>? Progress(string doing = "rendering")
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
