namespace Flyback.Gpu;

/// <summary>Which OpenGL a context speaks: desktop or ES, and the version.</summary>
internal readonly record struct ContextVersion(bool Es, int Major, int Minor)
{
    public override string ToString() => $"OpenGL{(Es ? " ES" : string.Empty)} {Major}.{Minor}";
}
