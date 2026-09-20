using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>What the program tells the shell it did.</summary>
/// <remarks>
/// Three answers rather than two, because "the patch is wrong" and "I could not
/// look at the patch" are different things to a script: the first is a result
/// and the second is a fault. A build that fails on the first has found
/// something; one that fails on the second has been pointed at the wrong path.
/// </remarks>
internal static class Exit
{
    public const int Ok = 0;

    /// <summary>The patch was read and something about it is wrong.</summary>
    public const int Problems = 1;

    /// <summary>The job could not be done at all — a file missing, a path unwritable.</summary>
    public const int Failed = 2;
}

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
