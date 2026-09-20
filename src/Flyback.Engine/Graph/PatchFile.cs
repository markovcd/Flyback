using Flyback.Core.Compile;
using Flyback.Core.Language;
using Flyback.Core.Render;

namespace Flyback.Core.Graph;

/// <summary>A patch and the files it names, wherever they were kept.</summary>
public readonly record struct Opened(
    Patch Patch,
    ISampleLibrary Samples,
    IImageLibrary Pictures);

/// <summary>
/// What opening a path came to: the patch to play, or null when there is none,
/// and every complaint made on the way, one line each.
/// </summary>
/// <remarks>
/// Complaints and a patch can come together: one short of a plugin still has
/// something to show.
/// </remarks>
public readonly record struct PatchOpen(Opened? Patch, IReadOnlyList<string> Problems);

/// <summary>What reading a document came to; <see cref="PatchOpen"/> without the files.</summary>
public readonly record struct PatchRead(Patch? Patch, IReadOnlyList<string> Problems);

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
    public static PatchOpen Open(FileInfo file)
    {
        if (!Bundled(file))
        {
            var read = Read(file);

            return new PatchOpen(
                read.Patch is { } loose
                    ? new Opened(
                        loose,
                        new SampleLibrary { Beside = file.DirectoryName },
                        new ImageLibrary { Beside = file.DirectoryName })
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

    /// <summary>Whether a path names a bundle rather than a patch.</summary>
    public static bool Bundled(FileInfo file) =>
        string.Equals(file.Extension, PatchBundle.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a path names the patch written as text rather than as a document.</summary>
    public static bool Sourced(FileInfo file) =>
        string.Equals(file.Extension, $".{PatchLanguage.FileExtension}", StringComparison.OrdinalIgnoreCase);

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
            problems.Add($"    {file.Name}:{issue.Line}:{issue.Column}: {issue.Message}");

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
