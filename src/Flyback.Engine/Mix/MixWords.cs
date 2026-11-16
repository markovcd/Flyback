using System.Globalization;
using System.Text.RegularExpressions;
using Flyback.Engine.Measure;

namespace Flyback.Engine.Mix;

/// <summary>How a mix check reads aloud: its findings and its figures, the same in the command line, the assistant and the editor.</summary>
public static partial class MixWords
{
    /// <summary>A kind of finding as a script reads it: <c>no-master-chain</c>.</summary>
    public static string Code(MixProblem problem) => Kebab().Replace(problem.ToString(), "$1-$2").ToLowerInvariant();

    /// <summary>A level in decibels, to one place.</summary>
    public static string Decibels(double level) =>
        double.IsNegativeInfinity(level) ? "-inf"
        : double.IsPositiveInfinity(level) ? "+inf"
        : level.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>An integrated loudness, or that there was too little to gate.</summary>
    public static string Lufs(double lufs) =>
        double.IsNegativeInfinity(lufs) ? "too quiet to gate" : Decibels(lufs) + " LUFS";

    /// <summary>Names run together: "A", "A and B", "A, B and C".</summary>
    public static string List(IEnumerable<string> names)
    {
        var all = names.ToArray();

        return all.Length switch
        {
            0 => string.Empty,
            1 => all[0],
            _ => string.Join(", ", all[..^1]) + " and " + all[^1],
        };
    }

    /// <summary>The window listened to and the figures, in a line.</summary>
    public static string Heard(MixReport report)
    {
        var window = $"Listened to {MeasurementWords.Number(report.Seconds)} s from {MeasurementWords.Number(report.From)} s, with nothing played in";
        var figures = report.Figures;

        if (double.IsNegativeInfinity(figures.TruePeak)) return window + ".";

        return $"{window}: {Lufs(figures.Lufs)}, true peak {Decibels(figures.TruePeak)} dBTP, "
            + $"{Decibels(figures.Sub)} dB below 60 Hz, {Decibels(figures.LowMids)} dB from 200 to 500 Hz, "
            + $"low-end side {Decibels(figures.LowEndWidth)} dB.";
    }

    /// <summary>The whole report as lines of text: the figures, then each finding marked by how much it matters.</summary>
    public static IEnumerable<string> Lines(MixReport report)
    {
        yield return Heard(report);

        if (report.Findings.Count == 0)
        {
            yield return "Nothing to flag.";
            yield break;
        }

        foreach (var finding in report.Findings)
            yield return $"{(finding.Severity == MixSeverity.Warning ? "warning" : "notice")} {finding.Code}: {finding.Text}";
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex Kebab();
}
