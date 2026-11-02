using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Says what may be written at one place in a text patch: the list the editor's
/// text view pops up, for a script or an agent with no window.
/// </summary>
internal static class CompleteCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<string>("patch")
        {
            Description = "The patch, written as text (." + PatchLanguage.FileExtension + "), or - to read it from standard input.",
        };

        var at = new Option<string>("--at")
        {
            Description = "Where the cursor is, as line:column, both counting from one.",
            Required = true,
        };

        var command = new Command("complete", "Say what may be written at one place in a text patch.")
        {
            patch, at, json,
        };

        command.SetAction(result =>
        {
            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var path = result.GetValue(patch)!;

            if (Where(result.GetValue(at)!) is not var (line, column))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName} complete: --at takes line:column, both whole numbers from one, like 3:14.");
                return Exit.Failed;
            }

            string source;

            if (path == "-")
            {
                source = Console.In.ReadToEnd();
            }
            else if (!File.Exists(path))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName} complete: {Path.GetFileName(path)} does not exist.");
                return Exit.Failed;
            }
            else
            {
                source = File.ReadAllText(path);
            }

            plugins.Ready();

            return Run(source, line, column, NodeCatalog.Current, result.GetValue(json), output);
        });

        return command;
    }

    public static int Run(string source, int line, int column, ModuleCatalog modules, bool json, TextWriter output)
    {
        var list = Completions.At(source, line, column, modules);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    line = list.Line,
                    column = list.Column,
                    typed = list.Typed,
                    items = list.Items.Select(i => new
                    {
                        label = i.Label,
                        kind = i.Kind.ToString().ToLowerInvariant(),
                        detail = i.Detail,
                        insert = i.Insert,
                    }),
                },
                Writing.Json));

            return Exit.Ok;
        }

        if (list.Items.Count == 0)
        {
            output.WriteLine("nothing to offer here.");
            return Exit.Ok;
        }

        var width = list.Items.Max(i => i.Label.Length);

        foreach (var item in list.Items)
            output.WriteLine($"{item.Label.PadRight(width)}  {item.Kind.ToString().ToLowerInvariant(),-10}  {item.Detail}");

        return Exit.Ok;
    }

    private static (int Line, int Column)? Where(string at) =>
        at.Split(':') is [var l, var c]
        && int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out var line) && line >= 1
        && int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out var column) && column >= 1
            ? (line, column)
            : null;
}
