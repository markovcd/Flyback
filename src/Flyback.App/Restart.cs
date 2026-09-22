using System.Diagnostics;
using System.Globalization;
using Flyback.App.Updates;

namespace Flyback.App;

/// <summary>
/// Starting this copy of Flyback again once the running one has gone, which is what
/// loads a plugin installed while it ran (ADR-0132).
/// </summary>
/// <remarks>
/// The new process waits for the old one before anything else, because a plugin
/// being replaced is one the old process has loaded, and until it exits Windows will
/// not let the new one move the replacement into place.
/// </remarks>
internal static class Restart
{
    /// <summary>What the new process is told to wait for the old one with, followed by its id.</summary>
    public const string AfterFlag = "--after";

    /// <summary>How long a closing window is given to be gone before the new one starts anyway.</summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);

    /// <summary>Starts this copy again, to open once this process has exited.</summary>
    public static void Launch()
    {
        string[] arguments = [AfterFlag, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)];

        if (Installation.Current() is { } copy)
        {
            copy.Launch(arguments);
            return;
        }

        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process.Start(start)?.Dispose();
    }

    /// <summary>
    /// Waits for the process <see cref="AfterFlag"/> names, if it names one, and gives
    /// back the arguments without it.
    /// </summary>
    public static string[] Awaited(string[] args)
    {
        var at = Array.IndexOf(args, AfterFlag);

        if (at < 0 || at + 1 >= args.Length) return args;

        if (int.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
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
