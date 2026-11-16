namespace Flyback.Engine.Mix;

/// <summary>The numbers a mix check judged the sound by.</summary>
/// <param name="Lufs">Integrated loudness as ITU-R BS.1770 gates it, or negative infinity when too quiet to gate.</param>
/// <param name="TruePeak">The true peak in dBTP, or negative infinity for silence.</param>
/// <param name="Clips">How many runs of two or more samples sat at full scale.</param>
/// <param name="Sub">The share of the power below 60 Hz, in dB.</param>
/// <param name="LowMids">The share of the power from 200 to 500 Hz, in dB.</param>
/// <param name="LowEndWidth">The side's power below 120 Hz against the middle's, in dB: very negative is mono.</param>
/// <param name="DcLeft">The mean of what is wired into the Output's left, or null when nothing is.</param>
/// <param name="DcRight">The same for its right.</param>
public sealed record MixFigures(
    double Lufs,
    double TruePeak,
    int Clips,
    double Sub,
    double LowMids,
    double LowEndWidth,
    double? DcLeft,
    double? DcRight)
{
    /// <summary>The figures of a patch with nothing to hear.</summary>
    public static MixFigures Silent { get; } = new(
        double.NegativeInfinity, double.NegativeInfinity, 0,
        double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, null, null);
}
