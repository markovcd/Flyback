using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>
/// The sound files a patch names, read once and kept.
/// </summary>
/// <remarks>
/// A cache rather than a loader, which is the point of its existing: every edit
/// recompiles the whole patch (ADR-0021), so a compiler that opened a file would
/// open it on every knob turn. Failures are remembered too — going back to the
/// disk sixty times a second to be told the same thing is the more expensive case
/// — and <see cref="Forget"/> is how a file that has since appeared gets another
/// chance. Not thread-safe, and it need not be: it is read on the thread that
/// compiles, and what comes out of it is immutable.
/// </remarks>
public sealed class SampleLibrary : ISampleLibrary
{
    private readonly Dictionary<string, (LoadedSample? Clip, WavFault Fault)> known =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a relative path is measured from — the folder the patch was opened
    /// from, or null while it has not been saved anywhere.
    /// </summary>
    /// <remarks>
    /// What lets a patch and its samples move together: a file beside the patch is
    /// named relatively and finds itself again wherever the pair is copied to, where
    /// an absolute path is left alone. Setting it clears what is known, because the
    /// same relative path means a different file once this changes.
    /// </remarks>
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

    public LoadedSample? Find(string path) => Look(path).Clip;

    public string Explain(string path) => Look(path).Fault switch
    {
        WavFault.Missing => "there is no file there.",
        WavFault.NotWave => "it is not a WAV file.",
        WavFault.Unsupported => "it is a WAV this cannot read — PCM only, 8 to 32 bit or float.",
        WavFault.Empty => "there is no audio in it.",
        _ => "it could not be read.",
    };

    /// <summary>
    /// Drops what is known about a path, or about everything when given none, so
    /// the next ask goes back to the disk.
    /// </summary>
    public void Forget(string? path = null)
    {
        if (path is null) known.Clear();
        else known.Remove(Full(path));
    }

    /// <summary>How many files this is holding, which is what a test asks to see a cache work.</summary>
    public int Count => known.Count(entry => entry.Value.Clip is not null);

    private (LoadedSample? Clip, WavFault Fault) Look(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, WavFault.Missing);

        var full = Full(path);

        if (known.TryGetValue(full, out var already)) return already;

        var clip = WavReader.Read(full, out var fault);
        return known[full] = (clip, fault);
    }

    /// <summary>
    /// The path as the filesystem will be asked for it: a relative one measured
    /// from <see cref="Beside"/>, and anything else left as it is.
    /// </summary>
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
            // A path with characters no filesystem will take. Handed back as it
            // came so the complaint quotes what the patch actually says, and the
            // read that follows will fail the ordinary way.
            return trimmed;
        }
        catch (PathTooLongException)
        {
            return trimmed;
        }
    }
}
