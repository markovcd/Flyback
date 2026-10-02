namespace Flyback.Engine.Measure;

/// <summary>Every measured output, and the window they were watched over.</summary>
/// <param name="From">Where the window started, in seconds.</param>
/// <param name="Seconds">How long it ran.</param>
/// <param name="Measurements">One per output socket, in patch order.</param>
/// <param name="Issues">What the compiler said about the patch while lowering it.</param>
public sealed record MeasureReport(
    double From,
    double Seconds,
    IReadOnlyList<Measurement> Measurements,
    IReadOnlyList<string> Issues);
