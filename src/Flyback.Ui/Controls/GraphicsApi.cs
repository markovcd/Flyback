namespace Flyback.App.Controls;

/// <summary>What draws the picture, named the way the status bar and the stats line say it.</summary>
public static class GraphicsApi
{
    /// <summary>The picture drawn by the interpreter or its IL, off the graphics card.</summary>
    public const string Processor = "CPU";

    /// <summary>The API under an OpenGL context. ANGLE is named for what it translates to.</summary>
    /// <param name="embedded">Whether the context is OpenGL ES.</param>
    /// <param name="renderer">What <c>GL_RENDERER</c> says, which is where ANGLE names its backend.</param>
    public static string Name(bool embedded, string? renderer)
    {
        if (renderer is null || !renderer.Contains("ANGLE", StringComparison.Ordinal))
            return embedded ? "OpenGL ES" : "OpenGL";

        if (renderer.Contains("Vulkan", StringComparison.OrdinalIgnoreCase)) return "Vulkan";
        if (renderer.Contains("Metal", StringComparison.OrdinalIgnoreCase)) return "Metal";

        return "Direct3D";
    }
}
