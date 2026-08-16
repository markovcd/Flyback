namespace Flyback.Plugins.Assist;

/// <summary>
/// What an assistant may spend before the workbench starts saying no.
/// </summary>
/// <remarks>
/// Every one of these is reported to the model when it is reached rather than
/// enforced behind its back. An agent told it has run out of room can finish
/// tidily and say what it did not get to; one that is silently refused keeps
/// trying the same thing until something else stops it.
/// </remarks>
/// <param name="MaxNodes">
/// A patch this large is already past anything the presets need, and the point
/// of the cap is that a loop which has lost the thread stops building rather
/// than making something the shell then has to compile and draw.
/// </param>
/// <param name="MaxToolCalls">The cost fuse. Tool calls are cheap; a run that never ends is not.</param>
/// <param name="LatestTime">The furthest into a patch a render may look, in seconds.</param>
/// <param name="WarmUpStep">
/// The interval frames are stepped at while warming. Feedback reads the frame
/// before it, so a render that jumped straight to its target time would show a
/// history that never happened.
/// </param>
public sealed record WorkbenchLimits(
    int MaxNodes = 120,
    int MaxToolCalls = 200,
    int FrameWidth = 320,
    int FrameHeight = 180,
    int MaxFrames = 4,
    double LatestTime = 8d,
    double WarmUpStep = 1d / 30d);
