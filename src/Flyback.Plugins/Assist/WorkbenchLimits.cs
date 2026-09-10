namespace Flyback.Plugins.Assist;

/// <summary>
/// What an assistant may spend before the workbench starts saying no.
/// </summary>
/// <remarks>
/// Every one of these is reported to the model when it is reached rather than
/// enforced behind its back: an agent told it has run out of room can finish tidily,
/// where one silently refused keeps trying the same thing.
/// </remarks>
/// <param name="MaxNodes">
/// A patch this large is past anything the presets need; the point of the cap is
/// that a loop which has lost the thread stops building.
/// </param>
/// <param name="MaxToolCalls">The cost fuse. Tool calls are cheap; a run that never ends is not.</param>
/// <param name="LatestTime">The furthest into a patch a render may look, in seconds.</param>
/// <param name="WarmUpStep">
/// The interval frames are stepped at while warming. Feedback reads the frame
/// before it, so a render that jumped to its target time would show a history that
/// never happened.
/// </param>
/// <param name="ListenRate">
/// The sample rate a <c>listen</c> renders at, which is not the rate the speakers
/// run at: what comes back is base64 in a request body, and 24 kHz still carries
/// every pitch this instrument makes.
/// </param>
/// <param name="LongestListen">
/// The most sound one call may render, in seconds. Short on purpose: a patch is
/// judged by ear in a second or two, and this is paid per turn.
/// </param>
public sealed record WorkbenchLimits(
    int MaxNodes = 120,
    int MaxToolCalls = 200,
    int FrameWidth = 320,
    int FrameHeight = 180,
    int MaxFrames = 4,
    double LatestTime = 8d,
    double WarmUpStep = 1d / 30d,
    int ListenRate = 24_000,
    double LongestListen = 4d);
