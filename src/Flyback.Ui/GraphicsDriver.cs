namespace Flyback.App;

/// <summary>What draws the window and the picture on Windows. Elsewhere there is only OpenGL.</summary>
public enum GraphicsDriver
{
    /// <summary>The graphics card's own OpenGL, and Direct3D where it will not start.</summary>
    OpenGl,

    /// <summary>Direct3D through ANGLE, which translates OpenGL and recompiles every shader as HLSL.</summary>
    Direct3D,
}
