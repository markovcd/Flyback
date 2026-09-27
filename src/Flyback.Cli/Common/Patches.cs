using Flyback.Core.Graph;

namespace Flyback.Cli.Common;

/// <summary>Reading a patch off disk, and saying why when that does not work.</summary>
internal static class Patches
{
    /// <summary>
    /// The patch a path names together with its files, or null with the reason
    /// already written to <paramref name="error"/>.
    /// </summary>
    public static Opened? Open(FileInfo file, TextWriter error)
    {
        var open = PatchFile.Open(file);

        Say(open.Problems, error);

        return open.Patch;
    }

    /// <summary>Whether a path names a bundle rather than a patch.</summary>
    public static bool Bundled(FileInfo file) => PatchFile.Bundled(file);

    /// <summary>Whether a path names the patch written as text rather than as a document.</summary>
    public static bool Sourced(FileInfo file) => PatchFile.Sourced(file);

    /// <summary>
    /// The patch in a file, or null with the reason already written to
    /// <paramref name="error"/>.
    /// </summary>
    public static Patch? Read(FileInfo file, TextWriter error)
    {
        var read = PatchFile.Read(file);

        Say(read.Problems, error);

        return read.Patch;
    }

    private static void Say(IReadOnlyList<string> problems, TextWriter error)
    {
        foreach (var problem in problems) error.WriteLine(problem);
    }
}
