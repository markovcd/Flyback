using Flyback.Core.Compile;
using Flyback.Engine.Graph;

namespace Flyback.Engine.Render;

/// <summary>
/// The files out of a bundle, read where they lie rather than unpacked: a library
/// for a patch whose sounds and pictures are bytes in memory.
/// </summary>
/// <remarks>
/// The archive counterpart of <see cref="SampleLibrary"/> and
/// <see cref="ImageLibrary"/>, one class since both read the same bytes. Decoded
/// on first ask and kept, since every edit recompiles the whole patch (ADR-0021).
/// </remarks>
/// <param name="files">The archive's entries, keyed by the path the patch names them by.</param>
/// <param name="behindSounds">
/// Where a sound the bundle lacks is looked for, or null. Lets an edited bundle
/// point at files on disk.
/// </param>
/// <param name="behindPictures">The same, for a picture.</param>
public sealed class BundleFiles(
    IReadOnlyDictionary<string, byte[]> files,
    SampleLibrary? behindSounds = null,
    IImageLibrary? behindPictures = null)
    : ISampleLibrary, IImageLibrary
{
    private readonly Dictionary<string, LoadedSample?> clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedImage?> pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedMidi?> songs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a bundle read out of a stream holds, ready to be compiled against.</summary>
    public static BundleFiles Of(LoadedBundle bundle) => new(bundle.Files);

    /// <summary>What the archive holds, as it holds it.</summary>
    public IReadOnlyDictionary<string, byte[]> Bytes => files;

    LoadedSample? ISampleLibrary.Find(string path) =>
        Cached<LoadedSample, SoundFault>(clips, path, Sound)
        ?? behindSounds?.Find(path);

    LoadedMidi? ISampleLibrary.FindMidi(string path)
    {
        if (!songs.TryGetValue(path, out var song))
            songs[path] = song = files.TryGetValue(path, out var bytes) ? MidiFileReader.Read(bytes, out _) : null;

        return song ?? behindSounds?.FindMidi(path);
    }

    string ISampleLibrary.ExplainMidi(string path) =>
        files.ContainsKey(path)
            ? "the bundle holds it, but it could not be read."
            : behindSounds?.ExplainMidi(path) ?? "the bundle does not hold it.";

    byte[]? ISampleLibrary.FindFile(string path) =>
        files.TryGetValue(path, out var bytes) ? bytes : behindSounds?.FindFile(path);

    LoadedImage? ISampleLibrary.FindPicture(string path) =>
        files.ContainsKey(path) ? ((IImageLibrary)this).Find(path) : behindSounds?.FindPicture(path);

    string ISampleLibrary.ExplainFile(string path) =>
        files.ContainsKey(path)
            ? "the bundle holds it, but it could not be read."
            : behindSounds?.ExplainFile(path) ?? "the bundle does not hold it.";

    LoadedImage? IImageLibrary.Find(string path) =>
        Cached<LoadedImage, PngFault>(pictures, path, PngReader.Read)
        ?? behindPictures?.Find(path);

    /// <summary>
    /// Why nothing came back — from whichever of the two was in a position to
    /// know. A path the archive holds is the archive's to explain; anything else
    /// belongs to whatever is behind it, and a bundle with nothing behind it says
    /// the only other thing there is to say.
    /// </summary>
    public string Explain(string path)
    {
        if (files.ContainsKey(path)) return "the bundle holds it, but it could not be read.";

        return behindPictures?.Explain(path)
            ?? behindSounds?.Explain(path)
            ?? "the bundle does not hold it.";
    }

    /// <summary>A sound, decoded with the ffmpeg the folder behind would use.</summary>
    private LoadedSample? Sound(Stream from, out SoundFault fault) =>
        SoundReader.Read(from, Ffmpeg.Resolve(behindSounds?.FfmpegPath), out fault);

    private T? Cached<T, TFault>(
        Dictionary<string, T?> known, string path, Reader<T, TFault> read)
        where T : class
    {
        if (known.TryGetValue(path, out var already)) return already;

        if (!files.TryGetValue(path, out var bytes)) return known[path] = null;

        return known[path] = read(new MemoryStream(bytes, writable: false), out _);
    }

    /// <summary>What both readers look like, so one lookup serves both.</summary>
    private delegate T? Reader<out T, TFault>(Stream from, out TFault fault);
}
