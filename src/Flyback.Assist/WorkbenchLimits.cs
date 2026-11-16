namespace Flyback.Assist;

/// <summary>
/// What an assistant may spend before the workbench starts saying no.
/// </summary>
/// <remarks>
/// Each limit is reported to the model when reached, so it can finish tidily
/// rather than retry.
/// </remarks>
/// <param name="MaxToolCalls">The cost fuse against a turn that never ends.</param>
/// <param name="FrameWidth">How wide a <c>render</c>'s frames are when no width is asked for; every frame is 16:9.</param>
/// <param name="FrameHeight">The height that goes with <paramref name="FrameWidth"/>, which also sets the aspect the sound is rendered at.</param>
/// <param name="SmallestFrameWidth">The narrowest frame a <c>render</c> may ask for.</param>
/// <param name="LargestFrameWidth">The widest frame a <c>render</c> may ask for.</param>
/// <param name="MaxFrames">The most frames one <c>render</c> draws.</param>
/// <param name="SheetPixels">The most pixels one <c>render</c>'s frames add up to; wider frames than fit are narrowed.</param>
/// <param name="LatestTime">How far past its start a <c>render</c> may look and how much sound <c>propose</c> checks for silence, in seconds.</param>
/// <param name="LatestStart">The furthest into a patch a <c>render</c>, <c>listen</c> or <c>measure</c> may start its window, in seconds.</param>
/// <param name="WarmUpLead">How far before a window's start a <c>render</c> begins stepping frames, so feedback has a history; nothing earlier is drawn.</param>
/// <param name="ListenLead">How far before a <c>listen</c> window's start the sound begins, so delays have a tail; nothing earlier is rendered.</param>
/// <param name="WarmUpStep">The frame interval stepped through before a render, so feedback has a real history.</param>
/// <param name="ListenRate">The sample rate a <c>listen</c> renders at, kept low for the request size.</param>
/// <param name="LongestListen">The most sound one call may render, in seconds.</param>
/// <param name="MeasuredOutputSeconds">
/// How many outputs times seconds one <c>measure</c> watches, since watching every output of
/// a big patch costs about as much as the patch itself, per output: a window over more outputs is shortened to fit.
/// </param>
internal sealed record WorkbenchLimits(
    int MaxToolCalls = 200,
    int FrameWidth = 320,
    int FrameHeight = 180,
    int SmallestFrameWidth = 160,
    int LargestFrameWidth = 1280,
    int MaxFrames = 8,
    int SheetPixels = 1280 * 720,
    double LatestTime = 8d,
    double LatestStart = 3600d,
    double WarmUpLead = 1d,
    double ListenLead = 4d,
    double WarmUpStep = 1d / 30d,
    int ListenRate = 24_000,
    double LongestListen = 4d,
    double MeasuredOutputSeconds = 1000d);
