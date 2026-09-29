namespace Flyback.Core.Compile;

/// <summary>
/// The arrays behind a <see cref="DelayState"/>, for a backend that runs the program
/// on them in place — see <see cref="JsLayout"/>.
/// </summary>
internal readonly record struct DelayArrays(
    float[][] Lines,
    int[] Positions,
    double[] Phases,
    double[] PreviousInputs,
    bool[] Running,
    double[] Units,
    double[] Planes,
    float[][] Traces,
    int[] TraceHeads);
