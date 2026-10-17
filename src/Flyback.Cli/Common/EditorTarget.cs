using System.Text.Json;
using Flyback.Cli.Models;
using Flyback.Host.Driving;

namespace Flyback.Cli.Common;

/// <summary>
/// Calls run on the patch open in the editor, over <see cref="EditorChannel"/>. Connected at
/// the first call and again after the editor goes away, so the editor may open later.
/// </summary>
/// <param name="name">The channel, <see cref="EditorChannel.Name"/> outside the tests.</param>
internal sealed class EditorTarget(string name) : IToolTarget, IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    private ChannelClient? client;
    private string greeting = "an MCP client";

    public string Describes =>
        "Every tool edits or reads the patch open in the Flyback editor, as the person sees it; "
        + "each edit is one the person can undo there.";

    public void Greet(string client) => greeting = client;

    public Task<ToolAnswered> Call(string tool, JsonElement arguments, CancellationToken cancel) =>
        Over(channel => channel.Call(tool, arguments, cancel), cancel);

    public Task<ToolAnswered> Say(string text, CancellationToken cancel) =>
        Over(channel => channel.Say(text, cancel), cancel);

    private async Task<ToolAnswered> Over(Func<ChannelClient, Task<ChannelReply>> ask, CancellationToken cancel)
    {
        if (client is null)
        {
            var (connected, why) = await ChannelClient.Connect(name, greeting, Wait, cancel).ConfigureAwait(false);

            if (connected is null) return new ToolAnswered(false, why);

            client = connected;
        }

        var reply = await ask(client).ConfigureAwait(false);

        // The editor closed: the next call connects again.
        if (reply.Id == 0)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }

        return new ToolAnswered(reply.Ok, reply.Text, Picture(reply.Png));
    }

    private static byte[]? Picture(string? png)
    {
        if (png is null) return null;

        try
        {
            return Convert.FromBase64String(png);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
    }
}
