namespace Flyback.Host;

/// <summary>What draws the picture on a Mac (ADR-0214). The window itself is Avalonia's.</summary>
public enum AppleDriver
{
    /// <summary>Apple's own API, which builds a patch's shader off the thread that draws the window.</summary>
    Metal,

    /// <summary>Apple's OpenGL, a layer over Metal that builds a large shader on the window's thread.</summary>
    OpenGl,
}
