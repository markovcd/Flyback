using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>
/// The second shell over the engine. Everything here is argument parsing and
/// where to write the answer; the work is Core's, exactly as it is for the
/// window.
/// </summary>
/// <remarks>
/// A separate program rather than a mode of the shell, and the reason is what it
/// does not carry: no Avalonia, so no X libraries, no fonts and no display on
/// the machine that runs it. A patch renders on a build server the same way it
/// renders on a desk.
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

        // Before anything reads a patch: a file may name modules that only a
        // plugin defines, and a catalogue settled after the fact would have let
        // it compile against the wrong one.
        NodeCatalog.Install(PluginHost.Load().Modules);

        var patch = new Argument<FileInfo>("patch") { Description = "The patch file to read." };
        var json = new Option<bool>("--json") { Description = "Write the answer as JSON instead of prose." };

        var root = new RootCommand($"{GlobalConstants.ApplicationName} — a patchable synthesiser, from the command line.")
        {
            Render(patch),
            Check(patch, json),
            Info(patch, json),
            Pack(patch),
        };

        var parsed = root.Parse(args);
        var code = parsed.Invoke();

        // Invoked either way, because that is what prints the complaint and the
        // help beneath it. But an argument nobody could parse is the shell being
        // held wrong rather than a patch being wrong, and the two should not
        // come back as the same number.
        return parsed.Errors.Count > 0 ? Exit.Failed : code;
    }

    private static Command Render(Argument<FileInfo> patch)
    {
        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = "Where to write it. The extension picks the format: .png, .wav or .avi.",
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
            Description = "JPEG quality inside an AVI, 1 to 100.",
            DefaultValueFactory = _ => JpegWriter.DefaultQuality,
        };

        var command = new Command("render", "Write a patch to a picture, a sound, or a clip of both.")
        {
            patch, output, size, at, seconds, fps, quality,
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
                result.GetValue(quality));

            return Task.FromResult(
                RenderCommand.Run(
                    loaded, options, Console.Error, Progress(), cancellation, samples, pictures));
        });

        return command;
    }

    /// <summary>
    /// Packs a patch and its files into a bundle. Not built on
    /// <see cref="Run"/> like the two below it: those answer questions about a
    /// patch and this writes a file, so it takes an output path rather than a
    /// <c>--json</c>.
    /// </summary>
    private static Command Pack(Argument<FileInfo> patch)
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
            patch, output,
        };

        command.SetAction(result => PackCommand.Run(
            result.GetRequiredValue(patch),
            result.GetRequiredValue(output),
            Console.Error,
            Console.Out));

        return command;
    }

    private static Command Check(Argument<FileInfo> patch, Option<bool> json) =>
        Run(new Command("check", "Compile a patch and report what is wrong with it."),
            patch,
            json,
            CheckCommand.Run);

    private static Command Info(Argument<FileInfo> patch, Option<bool> json) =>
        Run(new Command("info", "Say what a patch is made of and what each half of it costs."),
            patch,
            json,
            InfoCommand.Run);

    /// <summary>
    /// The two commands that read a patch and write about it, which differ only
    /// in what they write.
    /// </summary>
    private static Command Run(
        Command command,
        Argument<FileInfo> patch,
        Option<bool> json,
        Func<Patch, string, bool, TextWriter, TextWriter, ISampleLibrary?, IImageLibrary?, int> run)
    {
        command.Arguments.Add(patch);
        command.Options.Add(json);

        command.SetAction(result =>
        {
            var file = result.GetRequiredValue(patch);

            return Patches.Open(file, Console.Error) is not { } opened
                ? Exit.Failed
                : run(
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
        var parts = text.Split('x', 'X');

        if (parts.Length == 2
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var height)
            && width > 0
            && height > 0)
        {
            return (width, height);
        }

        result.AddError($"'{text}' is not a size — write it as WIDTHxHEIGHT, such as 1920x1080.");

        return (0, 0);
    }
}
