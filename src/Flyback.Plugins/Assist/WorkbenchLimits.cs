namespace Flyback.Plugins.Assist;

/// <summary>
/// What an assistant may spend before the workbench starts saying no.
/// </summary>
/// <remarks>
/// Each limit is reported to the model when reached, so it can finish tidily
/// rather than retry.
/// </remarks>
/// <param name="MaxToolCalls">The cost fuse against a turn that never ends.</param>
/// <param name="LatestTime">How far past its start a <c>render</c> may look, how far in a <c>listen</c> may start, and how much sound <c>propose</c> checks for silence, in seconds.</param>
/// <param name="LatestStart">The furthest into a patch a <c>render</c> may start its window, in seconds.</param>
/// <param name="WarmUpLead">How far before a window's start a <c>render</c> begins stepping frames, so feedback has a history; nothing earlier is drawn.</param>
/// <param name="WarmUpStep">The frame interval stepped through before a render, so feedback has a real history.</param>
/// <param name="ListenRate">The sample rate a <c>listen</c> renders at, kept low for the request size.</param>
/// <param name="LongestListen">The most sound one call may render, in seconds.</param>
internal sealed record WorkbenchLimits(
    int MaxToolCalls = 200,
    int FrameWidth = 320,
    int FrameHeight = 180,
    int MaxFrames = 4,
    double LatestTime = 8d,
    double LatestStart = 3600d,
    double WarmUpLead = 1d,
    double WarmUpStep = 1d / 30d,
    int ListenRate = 24_000,
    double LongestListen = 4d);
