using System.Globalization;

namespace Flyback.Core.Graph;

/// <summary>
/// A patch's length as it is typed and shown: minutes:seconds.hundredths, the way the
/// status bar tells the time, or plain seconds.
/// </summary>
internal static class PatchLength
{
    /// <summary>The shortest length a patch keeps, in seconds.</summary>
    public const double Shortest = 0.1;

    /// <summary>The longest, a day.</summary>
    public const double Longest = 24 * 60 * 60;

    /// <summary>
    /// <paramref name="seconds"/> rounded to the hundredth and held between
    /// <see cref="Shortest"/> and <see cref="Longest"/>. Null stays null, and so does
    /// a length that is not a number.
    /// </summary>
    public static double? Kept(double? seconds) =>
        seconds is { } s && double.IsFinite(s) ? Math.Round(Math.Clamp(s, Shortest, Longest), 2) : null;

    /// <summary>
    /// A length as typed: seconds, or minutes and seconds with a colon between, either
    /// with a fraction. Null for anything else, or for one outside what a patch keeps.
    /// </summary>
    public static double? Read(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var parts = typed.Trim().Split(':');
        if (parts.Length > 2) return null;

        var seconds = 0d;

        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) return null;

            // Only the seconds may carry a fraction or run past 59 on their own.
            if (i < parts.Length - 1 && number != Math.Floor(number)) return null;

            seconds = seconds * 60 + number;
        }

        seconds = Math.Round(seconds, 2);

        return seconds is >= Shortest and <= Longest ? seconds : null;
    }

    /// <summary>A length as it is shown: minutes:seconds.hundredths, e.g. 2:30.50.</summary>
    public static string Say(double seconds)
    {
        var hundredths = (long)Math.Round(Math.Max(seconds, 0d) * 100d);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}");
    }
}
