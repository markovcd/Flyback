using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Serves the assistant's tools over MCP to a coding assistant the person runs, editing a
/// patch file (ADR-0202). Hidden from help until the editor can be driven the same way.
/// </summary>
internal static class McpCommand
{
    public static Command Build(PluginRegistry plugins)
    {
        var patch = new Argument<FileInfo>("patch")
        {
            Description = $"The patch the tools edit, saved after each edit: {AskedPatch.Formats}. One that does not exist yet starts empty.",
        };

        var command = new Command(
            "mcp",
            "Serve the assistant's tools over MCP on standard input and output, for Claude Code, Codex or "
            + "another client you run, editing the patch file as `tool` does.")
        {
            patch,
        };

        command.Hidden = true;

        command.SetAction(async (result, cancel) =>
        {
            plugins.Ready();

            await new McpServer(plugins.Catalog, new ToolHistory(), result.GetValue(patch)!, result.InvocationConfiguration.Error)
                .Serve(Console.In, result.InvocationConfiguration.Output, cancel)
                .ConfigureAwait(false);

            return Exit.Ok;
        });

        return command;
    }
}
