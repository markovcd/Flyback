using System.IO.Pipes;

namespace Flyback.Host.Driving;

/// <summary>
/// The editor's end of <see cref="EditorChannel"/>: one client at a time, each request handed
/// to <paramref name="answer"/>, and any other client turned away with the reason.
/// </summary>
/// <param name="name">The pipe or socket, <see cref="EditorChannel.Name"/> outside the tests.</param>
internal sealed class ChannelServer(string name, Func<ChannelRequest, CancellationToken, Task<ChannelReply>> answer) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Task? accepting;
    private int busy;
    private int disposed;

    /// <summary>A client greeted the editor, by the name it gave.</summary>
    public event Action<string>? Connected;

    /// <summary>The client went away.</summary>
    public event Action? Disconnected;

    /// <summary>Listens; a client may connect once this returns.</summary>
    public void Start()
    {
        if (accepting is not null) return;

        var first = Listen();
        accepting = Task.Run(() => Accept(first, stop.Token));
    }

    private NamedPipeServerStream Listen() => new(
        name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, EditorChannel.Options);

    private async Task Accept(NamedPipeServerStream first, CancellationToken cancel)
    {
        for (var pipe = first; !cancel.IsCancellationRequested; pipe = Listen())
        {
            try
            {
                await pipe.WaitForConnectionAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return;
            }

            _ = Serve(pipe, cancel);
        }
    }

    private async Task Serve(NamedPipeServerStream pipe, CancellationToken cancel)
    {
        await using var held = pipe;
        using var lines = new ChannelLines(pipe);

        try
        {
            if (await lines.Read(cancel).ConfigureAwait(false) is not { } first
                || EditorChannel.Read<ChannelRequest>(first) is not { Hello: { } client })
            {
                return;
            }

            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                await lines.Write(EditorChannel.Write(new ChannelReply(0, false, "another program is driving the editor already.")), cancel).ConfigureAwait(false);
                return;
            }

            try
            {
                Connected?.Invoke(client);
                await lines.Write(EditorChannel.Write(new ChannelReply(0, true, "connected.")), cancel).ConfigureAwait(false);

                while (await lines.Read(cancel).ConfigureAwait(false) is { } line)
                {
                    var reply = EditorChannel.Read<ChannelRequest>(line) is { } request
                        ? await answer(request, cancel).ConfigureAwait(false)
                        : new ChannelReply(0, false, "that line is not a request.");

                    await lines.Write(EditorChannel.Write(reply), cancel).ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
                Disconnected?.Invoke();
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // A client that went away mid-line, or the editor closing.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        await stop.CancelAsync().ConfigureAwait(false);

        if (accepting is not null) await accepting.ConfigureAwait(false);

        stop.Dispose();
    }
}
