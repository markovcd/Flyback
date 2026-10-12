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

    private readonly ReadAhead<(LoadedImage? Picture, PngFault Fault)> ahead;

    public ImageLibrary() => ahead = new(known);

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

        ahead.Abandon();
        known.Remove(path);
    }

    /// <summary>
    /// Reads the pictures among <paramref name="paths"/> not known yet on a worker, so a
    /// compile after it finds them here. Complete at once where there are none.
    /// </summary>
    /// <remarks>Called on a thread with a synchronization context, which is the one the reads are kept on.</remarks>
    public Task Preload(IEnumerable<string> paths)
    {
        return ahead.Preload(paths, path => PatchPaths.Resolve(path, Beside, Library), full =>
        {
            var picture = PngReader.Read(full, out var fault);

            return (picture, fault);
        });
    }

    private void Clear()
    {
        ahead.Abandon();
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
