namespace Flyback.Plugins.Assist;

/// <summary>
/// What an assistant may spend before the workbench starts saying no.
/// </summary>
/// <remarks>
/// Each limit is reported to the model when reached, so it can finish tidily
/// rather than retry.
/// </remarks>
/// <param name="MaxToolCalls">The cost fuse against a run that never ends.</param>
/// <param name="LatestTime">The furthest into a patch a render may look, in seconds.</param>
/// <param name="WarmUpStep">The frame interval stepped through before a render, so feedback has a real history.</param>
/// <param name="ListenRate">The sample rate a <c>listen</c> renders at, kept low for the request size.</param>
/// <param name="LongestListen">The most sound one call may render, in seconds.</param>
public sealed record WorkbenchLimits(
    int MaxToolCalls = 200,
    int FrameWidth = 320,
    int FrameHeight = 180,
    int MaxFrames = 4,
    double LatestTime = 8d,
    double WarmUpStep = 1d / 30d,
    int ListenRate = 24_000,
    double LongestListen = 4d);
