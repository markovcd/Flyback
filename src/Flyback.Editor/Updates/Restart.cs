using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Flyback.Editor.Updates;

/// <summary>
/// Starting this copy of Flyback again once the running one has gone, which is what
/// loads a plugin installed while it ran (ADR-0132).
/// </summary>
/// <remarks>
/// The new process waits for the old one before anything else, because a plugin
/// being replaced is one the old process has loaded, and until it exits Windows will
/// not let the new one move the replacement into place.
/// </remarks>
internal static partial class Restart
{
    /// <summary>What the new process is told to wait for the old one with, followed by its id.</summary>
    public const string AfterFlag = "--after";

    /// <summary>What a shared preset the new process should open again is named by, followed by its id.</summary>
    public const string SharedFlag = "--shared";

    /// <summary>How long a closing window is given to be gone before the new one starts anyway.</summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts this copy again, to open once this process has exited, with
    /// <paramref name="open"/> for it to open when it does — a file on disk, or a preset
    /// the site shared, which has no file to name.
    /// </summary>
    public static void Launch(Reopen? open = null)
    {
        var arguments = Arguments(Environment.ProcessId, open);

        if (Installation.Current() is { } copy)
        {
            copy.Launch(arguments);
            return;
        }

        // Not on the terminal this one was started from, which may close as this exits.
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process.Start(start)?.Dispose();
    }

    /// <summary>
    /// What the new process is started with: the wait, and a patch for it to open where
    /// there is one.
    /// </summary>
    /// <remarks>
    /// The patch goes on as a plain argument, which is what a file opened with Flyback
    /// already arrives as, so the launch that receives it needs to know nothing about
    /// having been restarted.
    /// </remarks>
    internal static string[] Arguments(int process, Reopen? open) =>
    [
        AfterFlag,
        process.ToString(CultureInfo.InvariantCulture),
        .. open switch
        {
            { Shared: { Length: > 0 } shared } => [SharedFlag, shared],
            { Path: { Length: > 0 } path } => new[] { path },
            _ => [],
        },
    ];

    /// <summary>
    /// The id a launch was told to open again from the preset site, and the arguments
    /// without it, so what is left is read the way any launch's arguments are.
    /// </summary>
    public static (string? Shared, string[] Without) Shared(string[] args)
    {
        var at = Array.IndexOf(args, SharedFlag);

        return at < 0 || at + 1 >= args.Length
            ? (null, args)
            : (args[at + 1], [.. args[..at], .. args[(at + 2)..]]);
    }

    /// <summary>
    /// Leaves the terminal session a restart was started in, on macOS and Linux: a
    /// terminal that ran the old process hangs up its session as that exits, which
    /// would end this one with it.
    /// </summary>
    public static void Detach(string[] args)
    {
        if (Array.IndexOf(args, AfterFlag) < 0) return;

        // Fails only for a process group leader, which a process just started is not.
        if (OperatingSystem.IsMacOS()) _ = MacSetsid();
        else if (OperatingSystem.IsLinux()) _ = LinuxSetsid();
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "setsid")]
    private static partial int MacSetsid();

    [LibraryImport("libc.so.6", EntryPoint = "setsid")]
    private static partial int LinuxSetsid();

    /// <summary>
    /// Waits for the process <see cref="AfterFlag"/> names, if it names one, and gives
    /// back the arguments without it.
    /// </summary>
    public static string[] Awaited(string[] args)
    {
        var at = Array.IndexOf(args, AfterFlag);

        if (at < 0 || at + 1 >= args.Length) return args;

        // Positive, since no process has id 0: on Unix it names the caller's own
        // process group, which would read as alive for the whole wait.
        if (int.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.WaitForExit(LongestWait);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or not ours to watch, in which case it is not what asked.
            }
        }

        return [.. args[..at], .. args[(at + 2)..]];
    }
}
