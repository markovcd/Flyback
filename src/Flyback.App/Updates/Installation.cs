using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Flyback.App.Updates;

/// <summary>
/// Where a copy of Flyback lives: the folder a release unpacks to, or on macOS the
/// application bundle around it.
/// </summary>
/// <param name="Root">What an update replaces the contents of.</param>
/// <param name="Executable">The shell's executable, relative to <paramref name="Root"/>.</param>
/// <param name="Rid">Which platform's package this copy was published from.</param>
/// <param name="Bundle">Whether <paramref name="Root"/> is a macOS <c>.app</c>.</param>
internal sealed record Installation(string Root, string Executable, string Rid, bool Bundle)
{
    /// <summary>The name a package's bundle has, whatever the installed one was renamed to.</summary>
    public const string BundleName = "Flyback.app";

    /// <summary>
    /// The copy that is running, or null for one that cannot be told apart from
    /// its surroundings — a Mac build run from outside a bundle, say.
    /// </summary>
    public static Installation? Current()
    {
        if (Environment.ProcessPath is not { } process) return null;

        var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var rid = RuntimeInformation.RuntimeIdentifier;

        if (!OperatingSystem.IsMacOS())
            return new Installation(directory, Path.GetRelativePath(directory, process), rid, Bundle: false);

        // Contents/MacOS inside something.app — nothing else on a Mac is a copy
        // that could be replaced as a whole.
        var contents = Path.GetDirectoryName(directory);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);

        if (Path.GetFileName(directory) != "MacOS" || Path.GetFileName(contents) != "Contents"
            || bundle is null || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return null;

        return new Installation(bundle, Path.GetRelativePath(bundle, process), rid, Bundle: true);
    }

    /// <summary>
    /// Where this platform's copy sits inside a package unpacked to
    /// <paramref name="unpacked"/>: the release workflow zips each platform's
    /// folder under its identifier, and a Mac's holds the bundle.
    /// </summary>
    public string PayloadIn(string unpacked) =>
        Bundle ? Path.Combine(unpacked, Rid, BundleName) : Path.Combine(unpacked, Rid);

    /// <summary>
    /// Whether this user may change the copy — not where it was put somewhere only
    /// an administrator writes, and not a Mac bundle run from where the Finder
    /// translocated it, which is read-only.
    /// </summary>
    public bool Writable()
    {
        var probe = Path.Combine(Root, $".update-probe-{Guid.NewGuid():N}");

        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether any other process is running out of this copy: a second window, or
    /// the command line exporting a take (ADR-0078). Files it has open cannot be
    /// replaced on Windows, and on every platform it would go on running the old
    /// version beside the new one.
    /// </summary>
    public bool InUseByAnother()
    {
        var shell = Path.GetFileNameWithoutExtension(Executable);
        var prefix = Root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var name in new[] { shell, "flyback-cli" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;

                    try
                    {
                        if (process.MainModule?.FileName is { } path && path.StartsWith(prefix, comparison)) return true;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Gone since it was listed, or not ours to look at — in
                        // which case it is not running out of a folder we own.
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Starts the shell in this copy, with <paramref name="arguments"/>. A bundle is
    /// opened through Launch Services, so it is the application the Dock knows.
    /// </summary>
    public void Launch(IEnumerable<string> arguments)
    {
        var start = Bundle
            ? new ProcessStartInfo("open") { ArgumentList = { "-n", Root, "--args" } }
            : new ProcessStartInfo(Path.Combine(Root, Executable));

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        start.UseShellExecute = false;
        start.WorkingDirectory = Bundle ? Path.GetDirectoryName(Root) ?? Root : Root;

        Process.Start(start)?.Dispose();
    }
}
