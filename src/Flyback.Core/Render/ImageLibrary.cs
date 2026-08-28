using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>
/// The pictures a patch names, read once and kept.
/// </summary>
/// <remarks>
/// <see cref="SampleLibrary"/> for the other kind of file, and the same cache
/// for the same reasons: every edit recompiles the whole patch (ADR-0021), so a
/// compiler that opened a file would open it on every knob turn, and a patch
/// naming one that is not there is recompiled just as often as one naming a file
/// that is.
/// <para>
/// Two classes rather than one holding both. What they share is the caching, and
/// the caching is eleven lines; what they do not share is everything about what
/// a file is — the reader, the fault, the sentence a person is shown. Folding
/// them together would have meant a type parameter on all of it to save those
/// eleven lines.
/// </para>
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
        PngFault.Empty => "there is no picture in it.",
        _ => "it could not be read.",
    };

    /// <inheritdoc cref="SampleLibrary.Forget"/>
    public void Forget(string? path = null)
    {
        if (path is null) known.Clear();
        else known.Remove(Full(path));
    }

    /// <summary>How many files this is holding, which is what a test asks to see a cache work.</summary>
    public int Count => known.Count(entry => entry.Value.Picture is not null);

    private (LoadedImage? Picture, PngFault Fault) Look(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, PngFault.Missing);

        var full = Full(path);

        if (known.TryGetValue(full, out var already)) return already;

        var picture = PngReader.Read(full, out var fault);
        return known[full] = (picture, fault);
    }

    private string Full(string path)
    {
        var trimmed = path.Trim();

        try
        {
            return Beside is { Length: > 0 } folder && !Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(Path.Combine(folder, trimmed))
                : Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
        catch (PathTooLongException)
        {
            return trimmed;
        }
    }
}
