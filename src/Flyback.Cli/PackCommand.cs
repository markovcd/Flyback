using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>
/// Packs a patch and everything it names into one file.
/// </summary>
/// <remarks>
/// The command that makes a patch portable. A <c>.fbk</c> is a document full of
/// paths that mean something on the machine it was made on, and a <c>.fbkp</c> is
/// that document with the things it points at travelling beside it — so what goes
/// in an email, a repository or a build is one file rather than a folder somebody
/// has to keep together.
/// <para>
/// There is no unpack command, and that is the format doing its job rather than
/// an omission: a bundle is an ordinary zip, so anything on any machine already
/// opens one. What comes out is a patch and a <c>files</c> folder beside it,
/// which is a working patch because the paths inside it are relative.
/// </para>
/// </remarks>
internal static class PackCommand
{
    public static int Run(FileInfo file, FileInfo output, TextWriter error, TextWriter writer)
    {
        if (Patches.Read(file, error) is not { } patch) return Exit.Failed;

        // Where a relative path in the patch is measured from, which is the one
        // thing this has to know that the patch does not say.
        var beside = file.DirectoryName ?? ".";

        BundleReport report;

        try
        {
            using var archive = File.Create(output.FullName);

            report = PatchBundle.Write(archive, patch, path => Bytes(beside, path));
        }
        catch (Exception ex)
        {
            error.WriteLine($"flyback: {output.Name}: {ex.Message}");
            return Exit.Failed;
        }

        writer.WriteLine($"{output.Name}");
        writer.WriteLine($"  carried  {report.Carried.Count} file{(report.Carried.Count == 1 ? "" : "s")}");

        foreach (var carried in report.Carried) writer.WriteLine($"    {carried}");

        if (report.Whole) return Exit.Ok;

        // A bundle short of a file is still a bundle and still opens — what it is
        // not is self-contained, which is the whole point of having made one. So
        // it is written, and the exit code says the patch has something wrong
        // with it, exactly as check does about a missing file.
        error.WriteLine($"flyback: {output.Name}: {report.Missing.Count} file(s) could not be read.");

        foreach (var missing in report.Missing) error.WriteLine($"    {missing}");

        return Exit.Problems;
    }

    /// <summary>
    /// The bytes of a file the patch names, measured from beside the patch, and
    /// null for anything that cannot be read. Deliberately broad: a path that is
    /// nonsense, a file that has gone and one that is locked are all the same
    /// answer to a bundle, which reports them together.
    /// </summary>
    private static byte[]? Bytes(string beside, string path)
    {
        try
        {
            var full = Path.IsPathRooted(path) ? path : Path.Combine(beside, path);

            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
