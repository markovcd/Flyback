using System.Text.Json;
using Flyback.Cli.Commands;
using Flyback.Cli.Models;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli.Common;

/// <summary>Calls run on a patch file as <c>flyback-cli tool</c> runs them, and words written to <paramref name="log"/>.</summary>
internal sealed class FileTarget(PluginCatalog plugins, ToolHistory history, FileInfo patch, TextWriter log, string? settingsPath = null) : IToolTarget
{
    public string Describes =>
        $"Every tool edits or reads the patch {patch.FullName}, which is saved after each edit. "
        + "Undo with `flyback-cli tool undo` on that file.";

    public void Greet(string client)
    {
    }

    public async Task<ToolAnswered> Call(string tool, JsonElement arguments, CancellationToken cancel)
    {
        var said = new StringWriter();

        var ran = await ToolCommand.Dispatch(plugins, history, patch, tool, arguments, said, cancel, settingsPath).ConfigureAwait(false);

        if (ran is null) return new ToolAnswered(false, said.ToString().Trim());

        if (said.ToString() is { Length: > 0 } aside) await log.WriteAsync(aside).ConfigureAwait(false);

        return new ToolAnswered(ran.Outcome.Ok, ran.Outcome.Text, ran.Outcome.Png);
    }

    public async Task<ToolAnswered> Say(string text, CancellationToken cancel)
    {
        await log.WriteLineAsync(text).ConfigureAwait(false);
        await log.FlushAsync(cancel).ConfigureAwait(false);

        return new ToolAnswered(true, "said.");
    }
}
