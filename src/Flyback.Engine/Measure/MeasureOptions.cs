using Flyback.Core;
using Flyback.Engine.Render;

namespace Flyback.Engine.Measure;

/// <summary>What a measurement runs over: the window, and how finely each half is looked at.</summary>
/// <param name="Seconds">How long the patch runs.</param>
/// <param name="From">Where on its clock the window starts. Memory starts empty there.</param>
/// <param name="Modules">Whose outputs to measure, or null for every module's.</param>
/// <param name="SampleRate">The speakers' rate, oversampled as they play it.</param>
/// <param name="Columns">The picture's grid across.</param>
/// <param name="Rows">The picture's grid down.</param>
/// <param name="FramesPerSecond">How often the picture's grid is drawn.</param>
/// <param name="Interpreted">Runs on the interpreter rather than as IL, which gives the same numbers slower.</param>
public sealed record MeasureOptions(
    double Seconds = MeasureOptions.DefaultSeconds,
    double From = 0d,
    IReadOnlyCollection<Guid>? Modules = null,
    int SampleRate = GlobalConstants.SampleRate * AudioRenderer.DefaultOversample,
    int Columns = 32,
    int Rows = 18,
    int FramesPerSecond = 60,
    bool Interpreted = false)
{
    /// <summary>Long enough for a beat or two and an LFO's turn.</summary>
    public const double DefaultSeconds = 4d;

    /// <summary>The longest window, which is half an hour of sound.</summary>
    public const double MaxSeconds = 1_800d;
}
