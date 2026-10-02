using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Engine.Render;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// <c>flyback-cli shot</c>: a PNG of the editor's window with a patch open, its picture at
/// a chosen second, drawn with no screen.
/// </summary>
/// <remarks>
/// The request is checked here, where the shell's help and the preset list are, and drawn
/// by <c>Flyback --shot</c> beside this program, which has the window: this one keeps
/// carrying no UI framework.
/// </remarks>
internal static class ShotCommand
{
    /// <summary>Where the editor is expected: beside this program, in the folder both publish into.</summary>
    public static Func<string> Beside { get; set; } = () =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Flyback.exe" : "Flyback");

    public static Command Build(PluginRegistry plugins)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to open, as the editor would. Left out, give --preset instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = "The PNG to write.",
            Required = true,
        };

        var at = new Option<double>("--at")
        {
            Description = "Which second of the patch the picture is of. It runs the second and a half before it first.",
        };

        var size = new Option<(int Width, int Height)>("--size")
        {
            Description = "The window's size, as WIDTHxHEIGHT.",
            DefaultValueFactory = _ => (1440, 900),
            CustomParser = Size,
        };

        var select = new Option<string>("--select")
        {
            Description = "A box or module to select, by name, so the inspector shows it.",
        };

        var canvas = new Option<bool>("--canvas")
        {
            Description = "Show the canvas, even for a patch whose text is the document.",
        };

        var crop = new Option<bool>("--crop")
        {
            Description = "Write only the canvas around the modules. Shows the canvas.",
        };

        var measure = new Option<bool>("--measure")
        {
            Description = "Measure the outputs from --at, the selected module's or every module's, and pin them before the picture is taken.",
        };

        var command = new Command("shot", "Draw the editor's window with a patch open, at a chosen second, into a PNG.")
        {
            patch, preset, output, at, size, select, canvas, crop, measure,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;
            var file = result.GetValue(patch);
            var named = result.GetValue(preset);
            var into = result.GetRequiredValue(output);
            var (width, height) = result.GetValue(size);

            plugins.Ready();

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: give a patch or --preset, and not both.");
                return Exit.Failed;
            }

            if (!into.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {into.Name}: a shot is a PNG.");
                return Exit.Failed;
            }

            if (file is not null && Patches.Open(file, error) is null) return Exit.Problems;

            if (named is not null && ShippedPresets.Open(plugins.Catalog, named, error) is null) return Exit.Failed;

            List<string> arguments =
            [
                "--shot", into.FullName,
                "--at", result.GetValue(at).ToString(CultureInfo.InvariantCulture),
                "--size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"),
            ];

            if (result.GetValue(select) is { } selected) arguments.AddRange(["--select", selected]);
            if (result.GetValue(canvas)) arguments.Add("--canvas");
            if (result.GetValue(crop)) arguments.Add("--crop");
            if (result.GetValue(measure)) arguments.Add("--measure");

            arguments.AddRange(file is not null ? [file.FullName] : ["--preset", named!]);

            var code = Handover.Run(Beside(), "the editor", arguments, error);

            if (code == Exit.Ok) result.InvocationConfiguration.Output.WriteLine(into.Name);

            return code;
        });

        return command;
    }

    private static (int Width, int Height) Size(ArgumentResult result)
    {
        var text = result.Tokens[0].Value;

        if (FrameSize.Of(text) is { } size) return size;

        result.AddError(FrameSize.Refuse(text));
        return (0, 0);
    }
}
