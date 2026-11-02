using System.Diagnostics;
using System.Globalization;
using Flyback.Core;

namespace Flyback.Editor.Desktop;

/// <summary>
/// Whether a launch shows <see cref="SplashWindow"/> while its window is built: when getting this
/// far already took a while, or the last launch on this machine took long to show its window.
/// </summary>
internal static class SlowStart
{
    /// <summary>How long after the process began Avalonia may be ready before the launch counts as slow.</summary>
    public static readonly TimeSpan ReadyLate = TimeSpan.FromSeconds(0.75);

    /// <summary>How long a launch may take to show its window before the next one counts as slow.</summary>
    public static readonly TimeSpan WindowLate = TimeSpan.FromSeconds(1.5);

    /// <summary>Where the last launch's time to its window is kept.</summary>
    public static string File => Path.Combine(GlobalConstants.DataFolder, "last-start.txt");

    /// <param name="untilReady">How long this launch took to get Avalonia ready.</param>
    /// <param name="lastWindow">How long the last launch took to show its window, or null if unknown.</param>
    public static bool Splashes(TimeSpan untilReady, TimeSpan? lastWindow) =>
        untilReady >= ReadyLate || lastWindow >= WindowLate;

    private static long began = Stopwatch.GetTimestamp();

    /// <summary>Starts the clock <see cref="SinceLaunch"/> reads, first thing in <c>Main</c>.</summary>
    public static void Begin() => began = Stopwatch.GetTimestamp();

    /// <summary>
    /// The time since <c>Main</c> began. The process's own start time is no use here: on Linux it is
    /// counted from a boot time that can be seconds out in a virtual machine.
    /// </summary>
    public static TimeSpan SinceLaunch() => Stopwatch.GetElapsedTime(began);

    /// <summary>The time <see cref="Remember"/> kept in <paramref name="path"/>, or null when there is none to read.</summary>
    public static TimeSpan? Last(string path)
    {
        try
        {
            return int.TryParse(System.IO.File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                ? TimeSpan.FromMilliseconds(ms)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps <paramref name="took"/> for the next launch; a file that cannot be written only costs it the hint.</summary>
    public static void Remember(string path, TimeSpan took)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, ((int)took.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"startup: {ex.Message}");
        }
    }
}
