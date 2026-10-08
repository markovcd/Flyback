using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;

namespace Flyback.Engine.Graph;

/// <summary>
/// A patch off disk and the files it names, however they were named: a folder beside
/// the patch, or a bundle holding both.
/// </summary>
/// <remarks>
/// The one place either kind is opened, so nothing downstream knows there are two. A
/// bundle is read into memory and never unpacked, which is the case for reading one
/// this way: a build server holding a single file can render a patch whose
/// photographs it has never seen, and writes nothing but the frame.
/// </remarks>
public static class PatchFile
{
    /// <summary>The patch a path names together with its files.</summary>
    /// <param name="library">The library folder, where a file not beside the patch is looked for next.</param>
    public static PatchOpen Open(FileInfo file, string? library = null)
    {
        if (!Bundled(file))
        {
            var read = Read(file);

            return new PatchOpen(
                read.Patch is { } loose
                    ? new Opened(
                        loose,
                        new SampleLibrary { Beside = file.DirectoryName, Library = library },
                        new ImageLibrary { Beside = file.DirectoryName, Library = library })
                    : null,
                read.Problems);
        }

        try
        {
            using var archive = File.OpenRead(file.FullName);

            var bundle = PatchBundle.Read(archive);
            var files = BundleFiles.Of(bundle);

            return new PatchOpen(new Opened(bundle.Patch, files, files), []);
        }
        catch (Exception ex)
        {
            // The same breadth Read takes below, for the same reason: a file that
            // is not a bundle, one that is damaged and one that cannot be opened
            // are one sentence to whoever typed the path.
            return new PatchOpen(null, [$"{GlobalConstants.ApplicationName}: {file.Name}: {ex.Message}"]);
        }
    }

    /// <summary>
    /// The patch in a file's bytes, told apart by the name's extension as a path is: a
    /// bundle with the files it carries, a document, or the patch written as text.
    /// </summary>
    /// <remarks>
    /// For a page, a site or a server holding the file and no disk. Throws rather
    /// than reports, since what is said to whoever sent the bytes is theirs to word:
    /// <see cref="InvalidDataException"/> for a bundle that is not one, text that
    /// does not build or a document that is not a patch; <see cref="JsonException"/>
    /// for a document that is not JSON; <see cref="DecoderFallbackException"/> for
    /// bytes that are not text. A document from a later version or naming a module
    /// this build has not got reads without throwing, and its <see cref="LoadedBundle.Load"/> says so.
    /// </remarks>
    /// <param name="limit">The most a bundle may unpack to.</param>
    public static LoadedBundle Read(string fileName, byte[] bytes, ModuleCatalog? against = null, long limit = PatchBundle.UnpackedLimit)
    {
        if (Bundled(fileName))
        {
            using var archive = new MemoryStream(bytes, writable: false);

            return PatchBundle.Read(archive, against, limit);
        }

        var text = Strict.GetString(bytes).TrimStart(ByteOrderMark);

        if (Sourced(fileName))
        {
            var built = PatchLanguage.Build(text, against);
            if (!built.Ok) throw new InvalidDataException(string.Join('\n', built.Issues));

            return new LoadedBundle(built.Patch, NoFiles);
        }

        var load = PatchIO.Read(text, against);

        return new LoadedBundle(load.Patch, NoFiles, Load: load);
    }

    /// <summary>Whether a path names a bundle rather than a patch.</summary>
    public static bool Bundled(FileInfo file) => Bundled(file.Name);

    /// <inheritdoc cref="Bundled(FileInfo)"/>
    public static bool Bundled(string fileName) =>
        string.Equals(Path.GetExtension(fileName), PatchBundle.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a path names the patch written as text rather than as a document.</summary>
    public static bool Sourced(FileInfo file) => Sourced(file.Name);

    /// <inheritdoc cref="Sourced(FileInfo)"/>
    public static bool Sourced(string fileName) =>
        string.Equals(Path.GetExtension(fileName), $".{PatchLanguage.FileExtension}", StringComparison.OrdinalIgnoreCase);

    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>Throws on bytes that are not UTF-8, rather than reading them as question marks.</summary>
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly IReadOnlyDictionary<string, byte[]> NoFiles = ReadOnlyDictionary<string, byte[]>.Empty;

    /// <summary>The patch a source file describes, or none with every complaint said.</summary>
    /// <remarks>
    /// Refused whole where it does not read, which is where this differs from a
    /// document: a patch short of a plugin still has something to look at, and a
    /// source file that does not parse has produced nothing. Each complaint carries
    /// the line and column it is on.
    /// </remarks>
    private static PatchRead Built(FileInfo file, string text)
    {
        var load = PatchLanguage.Build(text);

        if (load.Ok) return new PatchRead(load.Patch, []);

        var problems = new List<string> { $"{GlobalConstants.ApplicationName}: {file.Name}: this patch does not read." };

        foreach (var issue in load.Issues)
            problems.Add($"    {file.Name}:{issue}");

        return new PatchRead(null, problems);
    }

    /// <summary>
    /// The patch in a file, with the reason when there is none. Every complaint the
    /// reader can make is one <see cref="PatchLoad"/> already words, and none is
    /// rephrased here: a shell should say what the program says.
    /// </summary>
    public static PatchRead Read(FileInfo file)
    {
        if (!file.Exists)
            return new PatchRead(null, [$"{GlobalConstants.ApplicationName}: {file.FullName}: no such file"]);

        PatchLoad load;

        try
        {
            var text = File.ReadAllText(file.FullName);

            if (Sourced(file)) return Built(file, text);

            load = PatchIO.Read(text);
        }
        catch (Exception ex)
        {
            // A file that is not a patch at all, or one this cannot get at.
            // Deliberately broad: every one of them is the same sentence to
            // whoever typed the path, and none of them should be a stack trace.
            return new PatchRead(null, [$"{GlobalConstants.ApplicationName}: {file.Name}: {ex.Message}"]);
        }

        if (load.IsComplete) return new PatchRead(load.Patch, []);

        var problems = new[]
        {
            $"{GlobalConstants.ApplicationName}: {file.Name}: this patch did not load completely.",
            load.Detail,
        };

        // Handed back all the same when there is something to work with. A
        // patch short of one plugin still compiles, still renders, and
        // still tells you more about itself than a refusal would — and
        // 'check' exists precisely to be run on a file like this.
        return new PatchRead(load.TooNew ? null : load.Patch, problems);
    }
}
