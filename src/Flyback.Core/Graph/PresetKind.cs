namespace Flyback.Core.Graph;

/// <summary>
/// What kind of thing a preset is, which is what the picker groups by.
/// </summary>
public enum PresetKind
{
    /// <summary>
    /// A patch with its owner's part still to do: the Output alone, or a player
    /// with no file chosen, which draws and plays nothing until one is.
    /// </summary>
    /// <remarks>
    /// First, though it is the least of them: somebody who means to build their
    /// own patch should not have to read past thirty that somebody else built.
    /// The players are here rather than among the ideas because a preset that
    /// opens silent and black reads as a fault anywhere else in the list, and
    /// because the window does not open on one of these unless it is told to.
    /// </remarks>
    Blank,

    /// <summary>
    /// One idea, at one sink: a patch about sound has no picture in it, and one
    /// about picture makes no sound.
    /// </summary>
    Idea,

    /// <summary>
    /// A patch about the two sinks meeting, where taking either half away would
    /// leave no point standing.
    /// </summary>
    Interplay,

    /// <summary>
    /// What one patch can be rather than what one module does. Exempt from the
    /// one-idea rule.
    /// </summary>
    Showcase,
}
