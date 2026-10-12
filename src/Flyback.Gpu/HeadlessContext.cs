using System.Globalization;
using System.Text.RegularExpressions;
using static Flyback.Gpu.GlConstants;

namespace Flyback.Gpu;

/// <summary>
/// An OpenGL context with no window to show: EGL with no surface on Linux, and a
/// hidden window's WGL context on Windows. Current on the thread that opened it.
/// </summary>
internal abstract partial class HeadlessContext : IDisposable
{
    private Gl? gl;

    /// <summary>The entry points, found once the context is current.</summary>
    public Gl Gl => gl ??= new Gl(GetProcAddress);

    public ContextVersion Version => Parse(Gl.GetString(GL_VERSION));

    /// <summary>The driver's name for the device, e.g. "NVIDIA GeForce RTX 4070 SUPER/PCIe/SSE2" or "llvmpipe".</summary>
    public string Renderer => Gl.GetString(GL_RENDERER) ?? "an unnamed GPU";

    /// <summary>
    /// A context on this machine, or null with <paramref name="why"/> saying what
    /// stood in the way.
    /// </summary>
    public static HeadlessContext? Open(out string? why)
    {
        if (OperatingSystem.IsWindows()) return WglContext.Open(out why);
        if (OperatingSystem.IsLinux()) return EglContext.Open(out why);

        why = "There is no headless OpenGL on this platform.";
        return null;
    }

    /// <summary>Makes this the calling thread's context.</summary>
    public abstract bool MakeCurrent();

    protected abstract IntPtr GetProcAddress(string name);

    public abstract void Dispose();

    /// <summary>
    /// The version out of <c>GL_VERSION</c>, which is "4.6.0 NVIDIA 560.94" on the
    /// desktop and "OpenGL ES 3.2 Mesa 24.0" on ES.
    /// </summary>
    internal static ContextVersion Parse(string? version)
    {
        if (version is null) return default;

        var es = version.StartsWith("OpenGL ES", StringComparison.Ordinal);
        var match = Numbers().Match(version);

        return match.Success
            ? new ContextVersion(
                es,
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : new ContextVersion(es, 0, 0);
    }

    [GeneratedRegex(@"(\d+)\.(\d+)")]
    private static partial Regex Numbers();
}
