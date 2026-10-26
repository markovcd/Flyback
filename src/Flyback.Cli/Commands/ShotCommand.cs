using System.CommandLine;
using System.Globalization;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
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
    private static string Beside() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Flyback.exe" : "Flyback");

    public static Command Build(PluginRegistry plugins)
    {
        var given = new PatchInput("draw", patch: "The patch to open, as the editor would. Left out, give --preset instead.");

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = "The PNG to write.",
        };

        var at = new Option<double>("--at")
        {
            Description = "Which second of the patch the picture is of. It runs the second and a half before it first.",
        };

        var size = new Option<(int Width, int Height)>("--size")
        {
            Description = "The window's size, as WIDTHxHEIGHT.",
            DefaultValueFactory = _ => (1440, 900),
            CustomParser = SizeArgument.Parse,
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

        var assistant = new Option<bool>("--assistant")
        {
            Description = "Open the assistant's column, showing the conversation the patch carries.",
        };

        var live = new Option<bool>("--live")
        {
            Description = "Give the picture the window with the patch over it, as live patching (Shift+F11) shows it: the modules, or the text for a patch whose text is the document.",
        };

        var editor = new Option<FileInfo>("--editor")
        {
            Description = "The editor program to draw with, in place of the one beside this program. A dev build keeps it in src/Flyback.Editor.Desktop/bin.",
        };

        var command = new Command("shot", "Draw the editor's window with a patch open, at a chosen second, into a PNG.")
        {
            given.Patch, given.Preset, given.Presets, output, at, size, select, canvas, crop, measure, assistant, live, editor,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;
            var (width, height) = result.GetValue(size);

            plugins.Ready();

            if (given.Listed(result, plugins)) return Exit.Ok;

            if (!given.Chosen(result, out var file, out var named)) return Exit.Failed;

            if (result.GetValue(output) is not { } into)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --out says where to write the PNG.");
                return Exit.Failed;
            }

            if (!into.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {into.Name}: a shot is a PNG.");
                return Exit.Failed;
            }

            if (result.GetValue(at) is not (>= 0d and <= RenderOptions.MostSeconds))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --at is a second of the patch, from 0 to {RenderOptions.MostSeconds:0}.");
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
            if (result.GetValue(assistant)) arguments.Add("--assistant");
            if (result.GetValue(live)) arguments.Add("--live");

            arguments.AddRange(file is not null ? [file.FullName] : ["--preset", named!]);

            var code = Handover.Run(result.GetValue(editor)?.FullName ?? Beside(), "the editor", arguments, error);

            if (code == Exit.Ok) result.InvocationConfiguration.Output.WriteLine(into.Name);

            return code;
        });

        return command;
    }
}
