namespace Flyback.Engine.Render;

/// <summary>How a picture's jagged edges are smoothed once it is drawn (ADR-0215).</summary>
public enum Smoothing
{
    /// <summary>Every pixel as the patch drew it.</summary>
    Off,

    /// <summary>A pass over the finished frame that blends across the edges it finds: <see cref="Fxaa"/>.</summary>
    Fast,

    /// <summary>Four samples a pixel: the frame drawn at twice the width and height and averaged down.</summary>
    Best,
}
