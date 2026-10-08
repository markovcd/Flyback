using System.Runtime.CompilerServices;
using Flyback.Core.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// The sound files, MIDI files and other files a patch names, read once and kept.
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
    private readonly Dictionary<string, (LoadedSample? Clip, SoundFault Fault)> known =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (LoadedMidi? Song, MidiFault Fault)> knownMidi =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (byte[]? Bytes, string Why)> knownFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (LoadedImage? Picture, string Why)> knownPictures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> lastWhy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The largest file <see cref="FindFile"/> reads, in bytes.</summary>
    public const int MostFileBytes = 16 * 1024 * 1024;

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
            Clear();
        }
    }

    /// <summary>
    /// The library folder, where a relative path not found beside the patch is
    /// looked for next, or null for none.
    /// </summary>
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

    /// <summary>
    /// The ffmpeg an MP3 is decoded with, as picked in the settings, or empty for
    /// the one on <c>PATH</c>. Setting it clears what is known, because an MP3
    /// refused for want of one may read now.
    /// </summary>
    public string FfmpegPath
    {
        get;
        set
        {
            if (string.Equals(field, value, StringComparison.OrdinalIgnoreCase)) return;

            field = value;
            Clear();
        }
    } = string.Empty;

    public LoadedSample? Find(string path) => Look(path).Clip;

    public LoadedMidi? FindMidi(string path) => LookMidi(path).Song;

    public string ExplainMidi(string path) => LookMidi(path).Fault switch
    {
        MidiFault.Missing => "there is no file there.",
        MidiFault.NotMidi => "it is not a MIDI file.",
        MidiFault.Unsupported => "it is a MIDI file this cannot read.",
        MidiFault.Elsewhere => "it is on another machine. Copy it beside the patch.",
        MidiFault.Empty => "there are no notes in it.",
        MidiFault.TooLong => $"it runs longer than {MidiFileReader.MostSeconds / 60f:0} minutes.",
        MidiFault.TooBig => "it is too big: more than 8 MB or 200,000 notes.",
        _ => "it could not be read.",
    };

    public byte[]? FindFile(string path) => Remember(path, LookFile(path)).Bytes;

    public LoadedImage? FindPicture(string path) => Remember(path, LookPicture(path)).Picture;

    public string ExplainFile(string path) => lastWhy.GetValueOrDefault(path, "it could not be read.");

    public string Explain(string path) => Look(path).Fault switch
    {
        SoundFault.Missing => "there is no file there.",
        SoundFault.NotSound => "it is not a WAV or an MP3.",
        SoundFault.Unsupported => "it is a WAV this cannot read — PCM only, 8 to 32 bit or float.",
        SoundFault.Elsewhere => "it is on another machine. Copy it beside the patch.",
        SoundFault.Empty => "there is no audio in it.",
        SoundFault.NoFfmpeg => "an MP3 is read by ffmpeg, and there is none on PATH. Pick one in Settings → Recording.",
        SoundFault.Undecoded => "ffmpeg could not read it.",
        _ => "it could not be read.",
    };

    /// <summary>
    /// Drops what is known about a path, or about everything when given none, so
    /// the next ask goes back to the disk.
    /// </summary>
    public void Forget(string? path = null)
    {
        if (path is null) Clear();
        else
        {
            known.Remove(path);
            knownMidi.Remove(path);
            knownFiles.Remove(path);
            knownPictures.Remove(path);
        }
    }

    private void Clear()
    {
        known.Clear();
        knownMidi.Clear();
        knownFiles.Clear();
        knownPictures.Clear();
    }

    /// <summary>How many files this is holding, which only a test asks, to see a cache work.</summary>
    public int Count => known.Count(entry => entry.Value.Clip is not null);

    private (LoadedSample? Clip, SoundFault Fault) Look(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, SoundFault.Missing);

        if (known.TryGetValue(path, out var already)) return already;

        if (PatchPaths.Resolve(path, Beside, Library) is not { } full) return known[path] = (null, SoundFault.Elsewhere);

        var clip = SoundReader.Read(full, Ffmpeg.Resolve(FfmpegPath), out var fault);
        return known[path] = (clip, fault);
    }

    private (LoadedMidi? Song, MidiFault Fault) LookMidi(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, MidiFault.Missing);

        if (knownMidi.TryGetValue(path, out var already)) return already;

        if (PatchPaths.Resolve(path, Beside, Library) is not { } full) return knownMidi[path] = (null, MidiFault.Elsewhere);

        var song = MidiFileReader.Read(full, out var fault);
        return knownMidi[path] = (song, fault);
    }

    private T Remember<T>(string path, T looked) where T : struct, ITuple
    {
        lastWhy[path] = (string)looked[1]!;
        return looked;
    }

    private (byte[]? Bytes, string Why) LookFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, "there is no file there.");

        if (knownFiles.TryGetValue(path, out var already)) return already;

        if (PatchPaths.Resolve(path, Beside, Library) is not { } full)
            return knownFiles[path] = (null, "it is on another machine. Copy it beside the patch.");

        try
        {
            var info = new FileInfo(full);

            if (!info.Exists) return knownFiles[path] = (null, "there is no file there.");

            if (info.Length > MostFileBytes)
                return knownFiles[path] = (null, $"it is larger than {MostFileBytes / 1024 / 1024} MB.");

            return knownFiles[path] = (File.ReadAllBytes(full), string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return knownFiles[path] = (null, "it could not be opened.");
        }
    }

    private (LoadedImage? Picture, string Why) LookPicture(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, "there is no file there.");

        if (knownPictures.TryGetValue(path, out var already)) return already;

        if (PatchPaths.Resolve(path, Beside, Library) is not { } full)
            return knownPictures[path] = (null, "it is on another machine. Copy it beside the patch.");

        var picture = PngReader.Read(full, out var fault);

        return knownPictures[path] = (picture, picture is null ? ImageLibrary.Explain(fault) : string.Empty);
    }
}
