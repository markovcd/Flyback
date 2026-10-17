using System.Text;

namespace Flyback.Host.Driving;

/// <summary>Lines in and out of a pipe, each read capped at <see cref="EditorChannel.LongestLine"/>.</summary>
internal sealed class ChannelLines(Stream pipe) : IDisposable
{
    private readonly StreamReader reader = new(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    private readonly StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
    private readonly char[] buffer = new char[64 * 1024];
    private readonly StringBuilder pending = new();
    private int start, end;

    /// <summary>The next line, or null at the end, or once a line runs past the cap.</summary>
    public async Task<string?> Read(CancellationToken cancel)
    {
        while (true)
        {
            var newline = Array.IndexOf(buffer, '\n', start, end - start);

            if (newline >= 0)
            {
                if (pending.Length + newline - start > EditorChannel.LongestLine) return null;

                pending.Append(buffer, start, newline - start);
                start = newline + 1;

                var line = pending.ToString();
                pending.Clear();

                return line;
            }

            pending.Append(buffer, start, end - start);

            if (pending.Length > EditorChannel.LongestLine) return null;

            start = 0;
            end = await reader.ReadAsync(buffer, cancel).ConfigureAwait(false);

            if (end == 0) return null;
        }
    }

    public async Task Write(string line, CancellationToken cancel)
    {
        await writer.WriteAsync(line.AsMemory(), cancel).ConfigureAwait(false);
        await writer.FlushAsync(cancel).ConfigureAwait(false);
    }

    public void Dispose()
    {
        reader.Dispose();
        writer.Dispose();
    }
}
