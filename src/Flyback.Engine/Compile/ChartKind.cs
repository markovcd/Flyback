namespace Flyback.Engine.Compile;

/// <summary>What a chart's buffer holds of the stretch of the past it was handed.</summary>
public enum ChartKind
{
    /// <summary>The stretch itself, resampled across the buffer: a Scope.</summary>
    Trace,

    /// <summary>Its frequency content on a log axis: an Analyzer.</summary>
    Spectrum,

    /// <summary>Two inputs drawn against each other as a phosphor trace: a Beam.</summary>
    Beam,
}
