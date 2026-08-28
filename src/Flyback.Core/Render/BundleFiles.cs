using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Render;

/// <summary>
/// The files out of a bundle, read where they lie rather than unpacked: a
/// library for a patch whose sounds and pictures are bytes in memory.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SampleLibrary"/> and <see cref="ImageLibrary"/> answer for a
/// folder; this answers for an archive. It is what lets a bundle be drawn
/// without being unpacked — the command line rendering one on a machine that has
/// none of its files loose, which is the case
/// [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) gave up
/// and this gets back.
/// </para>
/// <para>
/// One class for both kinds where the folder has two, and that is the difference
/// between them rather than an inconsistency: those two share only their
/// caching, and this shares its whole contents — one dictionary of bytes, looked
/// up by the same key, differing only in which reader is handed the stream. What
/// would have been duplicated here is the dictionary itself.
/// </para>
/// <para>
/// Decoded on the first ask and kept, exactly as the folder libraries do and for
/// the same reason: every edit recompiles the whole patch (ADR-0021), and a
/// picture decoded per knob turn is a picture decoded sixty times a second.
/// </para>
/// </remarks>
/// <param name="files">
/// The archive's entries, keyed by the path the patch names them by — which is
/// the path the packer wrote into it.
/// </param>
/// <param name="behindSounds">
/// Where a sound this does not hold is looked for instead, and null where there
/// is nowhere.
/// </param>
/// <param name="behindPictures">The same, for a picture.</param>
/// <remarks>
/// The two behinds are what make a bundle editable rather than only readable. An
/// archive holds what the patch named when it was packed, and somebody working
/// on it may point a module at something on their own machine a moment later —
/// that file is where they said it is, and asking the folder for it is the whole
/// of what has to happen. The command line passes neither, because nothing there
/// is going to add a file to a patch it is rendering.
/// </remarks>
public sealed class BundleFiles(
    IReadOnlyDictionary<string, byte[]> files,
    ISampleLibrary? behindSounds = null,
    IImageLibrary? behindPictures = null)
    : ISampleLibrary, IImageLibrary
{
    private readonly Dictionary<string, LoadedSample?> clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedImage?> pictures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a bundle read out of a stream holds, ready to be compiled against.</summary>
    public static BundleFiles Of(LoadedBundle bundle) => new(bundle.Files);

    /// <summary>What the archive holds, as it holds it — see <see cref="Held"/>.</summary>
    public IReadOnlyDictionary<string, byte[]> Bytes => files;

    /// <summary>Whether the archive holds a path, without decoding it.</summary>
    /// <remarks>
    /// What saving asks. A bundle written again is written out of the bytes that
    /// came in rather than out of what they decoded to: re-encoding a picture
    /// from the floats it was read into would quietly make a sixteen-bit file an
    /// eight-bit one and bake in the transparency that was multiplied away. So
    /// the compressed bytes are kept for as long as the bundle is open, which is
    /// the one thing this costs that a folder does not.
    /// </remarks>
    public bool Held(string path) => files.ContainsKey(path);

    LoadedSample? ISampleLibrary.Find(string path) =>
        Cached<LoadedSample, WavFault>(clips, path, WavReader.Read)
        ?? behindSounds?.Find(path);

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
