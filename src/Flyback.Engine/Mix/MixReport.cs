namespace Flyback.Engine.Mix;

/// <summary>What a mix check found, and the window it listened to.</summary>
/// <param name="From">Where the window started, in seconds.</param>
/// <param name="Seconds">How long it ran.</param>
/// <param name="Figures">The numbers the sound was judged by.</param>
/// <param name="Findings">What looks wrong, warnings first.</param>
/// <param name="Issues">What the compiler said about the patch.</param>
public sealed record MixReport(
    double From,
    double Seconds,
    MixFigures Figures,
    IReadOnlyList<MixFinding> Findings,
    IReadOnlyList<string> Issues)
{
    /// <summary>Whether anything found is a warning.</summary>
    public bool Warned => Findings.Any(f => f.Severity == MixSeverity.Warning);
}
