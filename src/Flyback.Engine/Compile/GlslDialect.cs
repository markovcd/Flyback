namespace Flyback.Engine.Compile;

/// <summary>
/// Which GLSL the context speaks. The two differ only in their opening lines, but
/// a shader written for one will not compile on the other, and which one you get
/// is decided by the windowing platform.
/// </summary>
public enum GlslDialect
{
    /// <summary>GLSL ES 3.00, which is what ANGLE on Windows and GLES on Linux give you.</summary>
    GlslEs300,

    /// <summary>GLSL 1.50, which is what a desktop GL 3.2 core context gives you.</summary>
    Glsl150,
}
