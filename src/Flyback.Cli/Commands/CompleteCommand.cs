using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
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
        var given = new CaretInput();

        var command = new Command("complete", "Say what may be written at one place in a text patch.")
        {
            given.Patch, given.At, json,
        };

        command.SetAction(result =>
        {
            if (!given.Read(result, "complete", result.InvocationConfiguration.Error, out var source, out var line, out var column))
                return Exit.Failed;

            plugins.Ready();

            return Run(source, line, column, NodeCatalog.Current, result.GetValue(json), result.InvocationConfiguration.Output);
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
}
