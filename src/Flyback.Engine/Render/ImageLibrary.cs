using Flyback.Core.Compile;

namespace Flyback.Core.Render;

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

    /// <inheritdoc cref="SampleLibrary.Beside"/>
    public string? Beside
    {
        get;
        set
        {
            if (string.Equals(field, value, StringComparison.OrdinalIgnoreCase)) return;

            field = value;
            known.Clear();
        }
    }

    public LoadedImage? Find(string path) => Look(path).Picture;

    public string Explain(string path) => Look(path).Fault switch
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
        if (path is null) known.Clear();
        else if (PatchPaths.Resolve(path, Beside) is { } full) known.Remove(full);
    }

    /// <summary>How many files this is holding, which is what a test asks to see a cache work.</summary>
    public int Count => known.Count(entry => entry.Value.Picture is not null);

    private (LoadedImage? Picture, PngFault Fault) Look(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, PngFault.Missing);

        if (PatchPaths.Resolve(path, Beside) is not { } full) return (null, PngFault.Elsewhere);

        if (known.TryGetValue(full, out var already)) return already;

        var picture = PngReader.Read(full, out var fault);
        return known[full] = (picture, fault);
    }
}
