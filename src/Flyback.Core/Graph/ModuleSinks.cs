namespace Flyback.Core.Graph;

/// <summary>
/// Which of the two programs evaluates a module as it is meant to be.
/// </summary>
/// <remarks>
/// Every module is compiled for both sinks — a patch is one graph (ADR-0022) —
/// so this changes nothing about what is emitted. It names what the catalog
/// only ever said in prose: a Filter is a wire on the video path because it has
/// no memory to run in, a Meter reads nothing where no sound is running, and a
/// Quantiser's hold cannot hold across an evaluation the screen does not have.
/// Written down so the palette can badge it and the handbook can carry it. It is
/// not what decides which sink a program reaches: that falls out of the walk.
/// </remarks>
public enum ModuleSinks
{
    /// <summary>The same arithmetic at both, which is nearly everything.</summary>
    Both,

    /// <summary>
    /// Wants a memory, so the screen gets something simpler: a Filter is a wire
    /// there, an envelope hands over its gate, and a hold does not hold.
    /// </summary>
    Audio,

    /// <summary>
    /// Wants a pixel, a frame before this one, or a stretch of what the speakers
    /// played — so the speakers' own program never reads it.
    /// </summary>
    Video,
}
