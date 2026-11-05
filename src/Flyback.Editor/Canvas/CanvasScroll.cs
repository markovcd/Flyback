namespace Flyback.Editor.Canvas;

/// <summary>What a scroll over the canvas does to the view (ADR-0226).</summary>
public enum CanvasScroll
{
    /// <summary>
    /// On a Mac a mouse wheel zooms and a trackpad pans; elsewhere the wheel zooms.
    /// </summary>
    Auto,

    /// <summary>Every scroll pans; Ctrl or Cmd with it zooms.</summary>
    Pan,

    /// <summary>Every scroll zooms.</summary>
    Zoom,
}
