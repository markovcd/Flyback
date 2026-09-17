using System.Diagnostics;

namespace Flyback.Core.Render;

/// <summary>
/// Finding ffmpeg, and saying what was found. Nothing here encodes anything —
/// that is <see cref="FfmpegClipWriter"/>; this is only the answer to "is it on
/// this machine, and where".
/// </summary>
/// <remarks>
/// A program rather than a library, which is the whole reason it is allowed here
/// under ADR-0019: nothing is linked, nothing is deployed, and a machine without
/// it writes the formats this program writes itself.
/// </remarks>
public static class Ffmpeg
{
    /// <summary>How long a version check is given before it is treated as no answer.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>What the executable is called here, which is the only part of this that is per-platform.</summary>
    public static string FileName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>
    /// The first ffmpeg on <c>PATH</c>, or null where there is none. Every entry
    /// is tried in the order the variable lists them, which is the order the
    /// shell would have run them in.
    /// </summary>
    /// <remarks>
    /// Walked rather than delegated: there is no BCL call for this, and running
    /// <c>where</c> or <c>which</c> to ask would be a second program to find
    /// before the first one.
    /// </remarks>
    public static string? OnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(path)) return null;

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // A PATH is whatever somebody put in it, and one bad entry — quotes
            // around it, a character no path may hold — must not stop the search
            // at the entry before the one that would have worked.
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), FileName);

                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Not a folder name. The next one may be.
            }
        }

        return null;
    }

    /// <summary>
    /// Where ffmpeg is: what somebody picked, or failing that whatever is on
    /// <c>PATH</c>. Null means there is none to be had, which is what makes a
    /// format needing one unavailable rather than broken.
    /// </summary>
    /// <param name="picked">
    /// A path chosen by hand, which wins over <c>PATH</c> because choosing it is
    /// what somebody does when the one on <c>PATH</c> is not the one they mean.
    /// A path that no longer exists falls back rather than failing.
    /// </param>
    public static string? Resolve(string? picked) =>
        !string.IsNullOrWhiteSpace(picked) && File.Exists(picked) ? picked : OnPath();

    /// <summary>
    /// What ffmpeg says it is — "ffmpeg version 7.1" and no more — or null where
    /// it would not run. Never throws: this is asked in order to show an answer,
    /// and "could not run it" is one of the answers.
    /// </summary>
    public static string? Version(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var first = process.StandardOutput.ReadLine();

            if (!process.WaitForExit(Patience))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            if (process.ExitCode != 0 || first is null) return null;

            // The first line carries the build's whole provenance — who packaged
            // it, which compiler, every configure flag — and none of that is
            // worth a line under a settings box. "ffmpeg version 7.1" is, so the
            // third word is cut at the hyphen a packager appends its own name
            // after.
            var words = first.Split(' ');

            if (words.Length < 3) return first;

            return $"{words[0]} {words[1]} {words[2].Split('-')[0]}";
        }
        catch (Exception)
        {
            // A path that is not a program, a program that will not start, a
            // machine that refuses to let it. All the same answer here.
            return null;
        }
    }
}
