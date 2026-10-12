namespace Flyback.Engine.Render;

/// <summary>
/// Reads a library's files on a worker before a compile asks for them, and keeps each result in the library's cache.
/// </summary>
/// <remarks>Used on a thread with a synchronization context, which is the one the reads are kept on.</remarks>
internal sealed class ReadAhead<T>(Dictionary<string, T> known)
{
    /// <summary>The files being read, so a second ask waits on the first.</summary>
    private readonly Dictionary<string, Task> reading = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Moved on by every <see cref="Abandon"/>, so a read that began before one is not kept after it.</summary>
    private int generation;

    /// <summary>
    /// Reads the files among <paramref name="paths"/> not known yet, each resolved to a full path and read on a
    /// worker. Complete at once where there are none.
    /// </summary>
    public Task Preload(IEnumerable<string> paths, Func<string, string?> resolve, Func<string, T> read)
    {
        var waits = new List<Task>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || known.ContainsKey(path)) continue;

            if (reading.TryGetValue(path, out var underway))
            {
                waits.Add(underway);
                continue;
            }

            if (resolve(path) is not { } full) continue;

            var task = Read(path, full, read);

            reading[path] = task;
            waits.Add(task);
        }

        return waits.Count == 0 ? Task.CompletedTask : Task.WhenAll(waits);
    }

    /// <summary>Lets go of every read underway, so none of them lands in the cache.</summary>
    public void Abandon()
    {
        generation++;
        reading.Clear();
    }

    private async Task Read(string path, string full, Func<string, T> read)
    {
        var era = generation;

        var result = await Task.Run(() => read(full)).ConfigureAwait(true);

        if (era != generation) return;

        reading.Remove(path);
        known.TryAdd(path, result);
    }
}
