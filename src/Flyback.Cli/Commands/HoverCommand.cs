using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Says what the word at one place in a text patch is, and the sockets of the call
/// the place is in: the tooltip and the signature line of the editor's text view,
/// for a script or an agent with no window.
/// </summary>
internal static class HoverCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var given = new CaretInput();

        var command = new Command("hover", "Say what the word at one place in a text patch is, and the sockets of the call it is in.")
        {
            given.Patch, given.At, json,
        };

        command.SetAction(result =>
        {
            if (!given.Read(result, "hover", result.InvocationConfiguration.Error, out var source, out var line, out var column))
                return Exit.Failed;

            plugins.Ready();

            return Run(source, line, column, NodeCatalog.Current, result.GetValue(json), result.InvocationConfiguration.Output);
        });

        return command;
    }

    public static int Run(string source, int line, int column, ModuleCatalog modules, bool json, TextWriter output)
    {
        var hover = Hovers.At(source, line, column, modules);
        var signature = Signatures.At(source, line, column, modules);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    hover = hover is null ? null : new { title = hover.Title, body = hover.Body },
                    signature = signature is null ? null : new
                    {
                        callee = signature.Callee,
                        title = signature.Title,
                        sockets = signature.Sockets.Select(s => new { name = s.Name, reads = s.Reads, help = s.Help }),
                        active = signature.Active,
                        piped = signature.Piped,
                    },
                },
                Writing.Json));

            return Exit.Ok;
        }

        if (hover is null && signature is null)
        {
            output.WriteLine("nothing to say here.");
            return Exit.Ok;
        }

        if (hover is not null)
        {
            output.WriteLine(hover.Title);

            foreach (var said in hover.Body.ReplaceLineEndings("\n").Split('\n'))
                output.WriteLine(said.Length > 0 ? "  " + said : string.Empty);
        }

        if (signature is not null)
        {
            if (hover is not null) output.WriteLine();

            output.WriteLine($"in {signature.Callee}(…), {signature.Title}:");

            for (var i = 0; i < signature.Sockets.Count; i++)
            {
                var socket = signature.Sockets[i];
                var mark = i == signature.Active ? "> " : i == signature.Piped ? "|>" : "  ";
                output.WriteLine($"  {mark} {socket.Name}{(socket.Reads.Length > 0 ? $"  {socket.Reads}" : string.Empty)}");
            }
        }

        return Exit.Ok;
    }
}
