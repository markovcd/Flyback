using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Host.Driving;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Serves the assistant's tools over MCP to a coding assistant the person runs, editing a
/// patch file or the patch open in the editor (ADR-0202).
/// </summary>
internal static class McpCommand
{
    public static Command Build(PluginRegistry plugins)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = $"The patch the tools edit, saved after each edit: {AskedPatch.Formats}. "
                + "Left out, they edit the patch open in the editor.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var editor = new Option<bool>("--editor")
        {
            Description = "Edit the patch open in the editor, which has to let programs on this computer edit it.",
        };

        var command = new Command(
            "mcp",
            "Serve the assistant's tools over MCP on standard input and output, for Claude Code, Codex or "
            + "another client you run, editing the patch file as `tool` does.")
        {
            patch, editor,
        };


        command.SetAction(async (result, cancel) =>
        {
            plugins.Ready();

            var error = result.InvocationConfiguration.Error;
            var file = result.GetValue(patch);

            if (file is not null && result.GetValue(editor))
            {
                error.WriteLine(AskedPatch.Complaint("mcp edits a patch file or the editor's patch, not both: leave out the file or --editor."));
                return Exit.Failed;
            }

            await using var open = file is null ? new EditorTarget(EditorChannel.Name) : null;

            IToolTarget target = open ?? (IToolTarget)new FileTarget(plugins.Catalog, new ToolHistory(), file!, error);

            await new McpServer(plugins.Catalog, target)
                .Serve(Console.In, result.InvocationConfiguration.Output, cancel)
                .ConfigureAwait(false);

            return Exit.Ok;
        });

        return command;
    }
}
