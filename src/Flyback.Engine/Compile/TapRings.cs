using Flyback.Core.Graph;

namespace Flyback.Engine.Compile;

/// <summary>How much of the past each tap's ring keeps: what its reader looks at, and no more.</summary>
/// <remarks>
/// A ring of the whole <see cref="DelayState.MaxWindowSeconds"/> is 24 MB at the
/// oversampled rate, and a Meter or Bridge with a frame-long window reads a few
/// thousand samples of it. So each ring is sized to its window; turning the window
/// knob rebuilds the program, and the new ring is filled from the old by
/// <see cref="DelayState.Adopt"/>.
/// </remarks>
internal static class TapRings
{
    /// <summary>Two evaluations past the window, so a read of the whole window never lands on the write head.</summary>
    private const int Slack = 2;

    /// <summary>What a charting module draws its ring as, and a measuring one as a trace.</summary>
    internal static ChartKind ChartOf(NodeDef def) =>
        def.ChartsBeam ? ChartKind.Beam : def.ChartsSpectrum ? ChartKind.Spectrum : ChartKind.Trace;

    /// <summary>The evaluations <paramref name="tap"/>'s ring holds, at <paramref name="sampleRate"/>.</summary>
    internal static int Length(TapSpec tap, int sampleRate)
    {
        var window = (long)Math.Ceiling(Math.Clamp(tap.Window, 0f, DelayState.MaxWindowSeconds) * (double)sampleRate);

        var reads = tap.Chart switch
        {
            // A beam reads a few persistences back, never more than its budget.
            ChartKind.Beam => Beams.MaxSpan,
            // A spectrum reads at least one shortest segment, however short the window.
            ChartKind.Spectrum => Math.Max(window, Spectra.ShortestSegment),
            _ => window,
        };

        return (int)Math.Clamp(reads + Slack, Slack + 1, DelayState.TraceSamples);
    }
}
