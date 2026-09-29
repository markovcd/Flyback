using System.Diagnostics;
using Flyback.Core;

namespace Flyback.Cli.Common;

/// <summary>Running another of Flyback's programs, the one beside this one, and answering with its exit code.</summary>
internal static class Handover
{
    /// <summary>
    /// Starts <paramref name="path"/> with <paramref name="arguments"/> and waits. Its output is
    /// this program's; <paramref name="what"/> names it where it is missing or will not start.
    /// </summary>
    public static int Run(string path, string what, IReadOnlyList<string> arguments, TextWriter error)
    {
        if (!File.Exists(path))
        {
            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: {what} is not here — looked for {path}. It ships beside this program.");

            return Exit.Failed;
        }

        var start = new ProcessStartInfo(path) { UseShellExecute = false };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using var started = Process.Start(start);

            if (started is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {path} would not start.");

                return Exit.Failed;
            }

            started.WaitForExit();

            return started.ExitCode;
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {path} would not start: {ex.Message}");

            return Exit.Failed;
        }
    }
}
