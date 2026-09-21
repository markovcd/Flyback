using System.Globalization;

namespace Flyback.App;

/// <summary>The time as the status bar and a take's progress show it.</summary>
internal static class StatusClock
{
    /// <summary>Seconds as minutes:seconds.hundredths, e.g. 1:05.25.</summary>
    internal static string Text(double seconds)
    {
        var hundredths = (long)Math.Floor(Math.Max(seconds, 0d) * 100d);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}");
    }
}
