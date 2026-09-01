using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Render;
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

/// <summary>
/// A patch off disk and the files it names, however they were named: a folder
/// beside the patch, or a bundle holding both.
/// </summary>
/// <remarks>
/// The one place either kind is opened, so nothing downstream of it knows there
/// are two. A bundle is read into memory and never unpacked — which is the whole
/// case for reading one this way: a build server holding a single file can render
/// a patch whose photographs and recordings it has never seen, and writes nothing
/// but the frame it was asked for.
/// </remarks>
internal readonly record struct Opened(
    Patch Patch,
    ISampleLibrary Samples,
    IImageLibrary Pictures);

/// <summary>Reading a patch off disk, and saying why when that does not work.</summary>
internal static class Patches
{
    /// <summary>
    /// The patch a path names together with its files, or null with the reason
    /// already written to <paramref name="error"/>.
    /// </summary>
    public static Opened? Open(FileInfo file, TextWriter error)
    {
        if (!Bundled(file))
        {
            return Read(file, error) is { } loose
                ? new Opened(
                    loose,
                    new SampleLibrary { Beside = file.DirectoryName },
                    new ImageLibrary { Beside = file.DirectoryName })
                : null;
        }

        try
        {
            using var archive = File.OpenRead(file.FullName);

            var bundle = PatchBundle.Read(archive);
            var files = BundleFiles.Of(bundle);

            return new Opened(bundle.Patch, files, files);
        }
        catch (Exception ex)
        {
            // The same breadth Read takes below, for the same reason: a file that
            // is not a bundle, one that is damaged and one that cannot be opened
            // are one sentence to whoever typed the path.
            error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Whether a path names a bundle rather than a patch.</summary>
    public static bool Bundled(FileInfo file) =>
        string.Equals(file.Extension, PatchBundle.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The patch in a file, or null with the reason already written to
    /// <paramref name="error"/>.
    /// </summary>
    /// <remarks>
    /// Every complaint the reader can make is one <see cref="PatchLoad"/>
    /// already words — a format from a newer build, a plugin that is not
    /// installed, a module that could not be built. None of them is rephrased
    /// here: the shell should say what the program says.
    /// </remarks>
    public static Patch? Read(FileInfo file, TextWriter error)
    {
        if (!file.Exists)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {file.FullName}: no such file");
            return null;
        }

        PatchLoad load;

        try
        {
            load = PatchIO.Read(File.ReadAllText(file.FullName));
        }
        catch (Exception ex)
        {
            // A file that is not a patch at all, or one this cannot get at.
            // Deliberately broad: every one of them is the same sentence to
            // whoever typed the path, and none of them should be a stack trace.
            error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name}: {ex.Message}");
            return null;
        }

        if (!load.IsComplete)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name}: this patch did not load completely.");
            error.WriteLine(load.Detail);

            // Handed back all the same when there is something to work with. A
            // patch short of one plugin still compiles, still renders, and
            // still tells you more about itself than a refusal would — and
            // 'check' exists precisely to be run on a file like this.
            if (load.TooNew) return null;
        }

        return load.Patch;
    }
}
