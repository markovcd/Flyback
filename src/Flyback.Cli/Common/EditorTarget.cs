using System.Text.Json;
using Flyback.Assist;
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
        "Every tool reads or edits your own copy of the patch open in the Flyback editor. Nothing reaches "
        + "the person's canvas until propose, which puts it there as one step they can undo.";

    public Delivery Delivery => Delivery.Proposal;

    public void Greet(string clientName) => greeting = clientName;

    public Task<ToolAnswered> Call(string tool, JsonElement arguments, CancellationToken cancel) =>
        Over(channel => channel.Call(tool, arguments, cancel), cancel);

    public Task<ToolAnswered> Say(string text, CancellationToken cancel) =>
        Over(channel => channel.Say(text, cancel), cancel);

    /// <summary>
    /// The editor's briefing, since it compiles with the plugins it loaded rather than this
    /// program's; null where no editor answers, or one too old to be asked.
    /// </summary>
    public async Task<string?> Briefing(CancellationToken cancel)
    {
        var answered = await Over(channel => channel.Brief(cancel), cancel).ConfigureAwait(false);

        return answered.Ok ? answered.Text : null;
    }

    private async Task<ToolAnswered> Over(Func<ChannelClient, Task<ChannelReply>> ask, CancellationToken cancel)
    {
        if (client is null)
        {
            var (connected, why) = await ChannelClient.Connect(name, greeting, Wait, cancel).ConfigureAwait(false);

            if (connected is null) return new ToolAnswered(false, why);

            client = connected;
        }

        ChannelReply reply;

        try
        {
            reply = await ask(client).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The client gave up on the call. Hanging up ends it in the editor too, which a
            // reply still on its way would otherwise answer to the next call.
            var gone = client;
            client = null;
            await gone.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        // The editor closed: the next call connects again.
        if (reply.Id == 0)
        {
            var gone = client;
            client = null;
            await gone.DisposeAsync().ConfigureAwait(false);
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
