namespace Flyback.Host;

/// <summary>The API the graphics card draws the picture through. Which a machine offers is <see cref="OutputSettings.DriversHere"/>.</summary>
public enum GraphicsDriver
{
    /// <summary>OpenGL: the graphics card's own on Windows, Apple's layer over Metal on a Mac.</summary>
    OpenGl,

    /// <summary>Direct3D through ANGLE, which translates OpenGL and recompiles every shader as HLSL. Windows only.</summary>
    Direct3D,

    /// <summary>Apple's own API, which builds a patch's shader off the thread that draws the window (ADR-0214).</summary>
    Metal,
}
