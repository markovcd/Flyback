using System.IO.Pipes;
using System.Text.Json;

namespace Flyback.Host.Driving;

/// <summary>A program's end of <see cref="EditorChannel"/>: connected and greeted, then one request at a time.</summary>
internal sealed class ChannelClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream pipe;
    private readonly ChannelLines lines;
    private int next;

    private ChannelClient(NamedPipeClientStream pipe)
    {
        this.pipe = pipe;
        lines = new ChannelLines(pipe);
    }

    /// <summary>A client greeted by the editor, or null with why not in <c>Why</c>.</summary>
    public static async Task<(ChannelClient? Client, string Why)> Connect(string name, string client, TimeSpan wait, CancellationToken cancel)
    {
        const string Closed = "no editor is open with \"Let a program on this computer edit the open patch\" on.";

        // A socket that is not there would only time out.
        if (Path.IsPathRooted(name) && !File.Exists(name)) return (null, Closed);

        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, EditorChannel.Options);

        try
        {
            await pipe.ConnectAsync(wait, cancel).ConfigureAwait(false);

            var connected = new ChannelClient(pipe);

            await connected.lines.Write(EditorChannel.Write(new ChannelRequest(0, Hello: client)), cancel).ConfigureAwait(false);

            if (await connected.lines.Read(cancel).ConfigureAwait(false) is { } line
                && EditorChannel.Read<ChannelReply>(line) is { } reply)
            {
                if (reply.Ok) return (connected, string.Empty);

                await connected.DisposeAsync().ConfigureAwait(false);
                return (null, reply.Text);
            }

            await connected.DisposeAsync().ConfigureAwait(false);
            return (null, "the editor closed the channel before answering.");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return (null, Closed);
        }
    }

    /// <summary>Calls <paramref name="tool"/> on the open patch.</summary>
    public Task<ChannelReply> Call(string tool, JsonElement arguments, CancellationToken cancel) =>
        Ask(new ChannelRequest(++next, Tool: tool, Arguments: arguments), cancel);

    /// <summary>Shows <paramref name="text"/> to the person at the editor.</summary>
    public Task<ChannelReply> Say(string text, CancellationToken cancel) =>
        Ask(new ChannelRequest(++next, Say: text), cancel);

    /// <summary>The reply to <paramref name="request"/>, or one numbered 0 where the channel closed.</summary>
    private async Task<ChannelReply> Ask(ChannelRequest request, CancellationToken cancel)
    {
        try
        {
            await lines.Write(EditorChannel.Write(request), cancel).ConfigureAwait(false);

            return await lines.Read(cancel).ConfigureAwait(false) is { } line && EditorChannel.Read<ChannelReply>(line) is { } reply
                ? reply
                : new ChannelReply(0, false, "the editor closed the channel.");
        }
        catch (IOException ex)
        {
            return new ChannelReply(0, false, $"the editor closed the channel: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
    {
        lines.Dispose();
        return pipe.DisposeAsync();
    }
}
