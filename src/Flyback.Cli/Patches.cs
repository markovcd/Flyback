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
            error.WriteLine($"flyback: {file.FullName}: no such file");
            return null;
        }

        PatchLoad load;

        try
        {
            load = PatchIo.Read(File.ReadAllText(file.FullName));
        }
        catch (Exception ex)
        {
            // A file that is not a patch at all, or one this cannot get at.
            // Deliberately broad: every one of them is the same sentence to
            // whoever typed the path, and none of them should be a stack trace.
            error.WriteLine($"flyback: {file.Name}: {ex.Message}");
            return null;
        }

        if (!load.IsComplete)
        {
            error.WriteLine($"flyback: {file.Name}: this patch did not load completely.");
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
