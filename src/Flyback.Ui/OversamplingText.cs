using System.Globalization;

namespace Flyback.Ui;

/// <summary>An oversampling factor as the status lines and Settings → Sound write it; 1× is none.</summary>
internal static class OversamplingText
{
    /// <summary>"no oversampling" at 1, otherwise e.g. "2× oversampling".</summary>
    internal static string Of(int factor) =>
        factor <= 1 ? "no oversampling" : string.Create(CultureInfo.InvariantCulture, $"{factor}× oversampling");

    /// <summary>A row of the Settings → Sound picker: "None" at 1, otherwise e.g. "2×".</summary>
    internal static string Choice(int factor) =>
        factor <= 1 ? "None" : string.Create(CultureInfo.InvariantCulture, $"{factor}×");
}
