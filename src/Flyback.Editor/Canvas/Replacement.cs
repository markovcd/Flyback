using Flyback.Engine.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>How the patch on the canvas came to be a different object.</summary>
internal enum Replacement
{
    /// <summary>A document arrived: nothing is behind it, and the view is framed on it.</summary>
    Opened,

    /// <summary>A step came back out of the history, which leaves the view where it was.</summary>
    Restored,

    /// <summary>The assistant rebuilt the patch, which is an edit and is framed like an arrival.</summary>
    Applied,
}