using Flyback.Core.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// The pictures a patch names, read once and kept.
/// </summary>
/// <remarks>
/// <see cref="SampleLibrary"/> for the other kind of file, and the same cache for the
/// same reason: every edit recompiles the whole patch (ADR-0021). Two classes rather
/// than one, because what they share is eleven lines of caching and what they do not
/// share is everything about what a file is — the reader, the fault, the sentence a
/// person is shown.
/// </remarks>
public sealed class ImageLibrary : IImageLibrary
{
    private readonly Dictionary<string, (LoadedImage? Picture, PngFault Fault)> known =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pictures <see cref="Preload"/> is reading, so a second ask waits on the first.</summary>
    private readonly Dictionary<string, Task> reading = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Moved on by every clear, so a read that began before one is not kept after it.</summary>
    private int generation;

    /// <inheritdoc cref="SampleLibrary.Beside"/>
    public string? Beside
    {
        get;
        set
        {
            if (string.Equals(field, value, StringComparison.OrdinalIgnoreCase)) return;

            field = value;
            Clear();
        }
    }

    /// <inheritdoc cref="SampleLibrary.Library"/>
    public string? Library
    {
        get;
        set
        {
            if (string.Equals(field, value, StringComparison.OrdinalIgnoreCase)) return;

            field = value;
            Clear();
        }
    }

    public LoadedImage? Find(string path) => Look(path).Picture;

    public string Explain(string path) => Explain(Look(path).Fault);

    /// <summary>What a fault means, said to the person who chose the file.</summary>
    internal static string Explain(PngFault fault) => fault switch
    {
        PngFault.Missing => "there is no file there.",
        PngFault.NotPng => "it is not a PNG.",
        PngFault.Unsupported => "it is a PNG this cannot read — 8 or 16 bit, and not interlaced.",
        PngFault.Corrupt => "the picture in it is damaged.",
        PngFault.Elsewhere => "it is on another machine. Copy it beside the patch.",
        PngFault.Empty => "there is no picture in it.",
        _ => "it could not be read.",
    };

    /// <inheritdoc cref="SampleLibrary.Forget"/>
    public void Forget(string? path = null)
    {
        if (path is null)
        {
            Clear();
            return;
        }

        generation++;
        reading.Clear();
        known.Remove(path);
    }

    /// <summary>
    /// Reads the pictures among <paramref name="paths"/> not known yet on a worker, so a
    /// compile after it finds them here. Complete at once where there are none.
    /// </summary>
    /// <remarks>Called on a thread with a synchronization context, which is the one the reads are kept on.</remarks>
    public Task Preload(IEnumerable<string> paths)
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

            if (PatchPaths.Resolve(path, Beside, Library) is not { } full) continue;

            var read = ReadAhead(path, full);

            reading[path] = read;
            waits.Add(read);
        }

        return waits.Count == 0 ? Task.CompletedTask : Task.WhenAll(waits);
    }

    private async Task ReadAhead(string path, string full)
    {
        var era = generation;

        var (picture, fault) = await Task.Run(() =>
        {
            var picture = PngReader.Read(full, out var fault);

            return (picture, fault);
        }).ConfigureAwait(true);

        if (era != generation) return;

        reading.Remove(path);
        known.TryAdd(path, (picture, fault));
    }

    private void Clear()
    {
        generation++;
        reading.Clear();
        known.Clear();
    }

    /// <summary>How many files this is holding, which only a test asks, to see a cache work.</summary>
    public int Count => known.Count(entry => entry.Value.Picture is not null);

    private (LoadedImage? Picture, PngFault Fault) Look(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, PngFault.Missing);

        if (known.TryGetValue(path, out var already)) return already;

        if (PatchPaths.Resolve(path, Beside, Library) is not { } full) return known[path] = (null, PngFault.Elsewhere);

        var picture = PngReader.Read(full, out var fault);
        return known[path] = (picture, fault);
    }
}
