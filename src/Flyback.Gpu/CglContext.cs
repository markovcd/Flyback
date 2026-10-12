using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Flyback.Gpu;

/// <summary>
/// A CGL context with no drawable: OpenGL 3.2 core, on the GPU where there is one
/// and on Apple's software renderer where there is not. Every frame goes to a
/// framebuffer of the renderer's own.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class CglContext : HeadlessContext
{
    private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    private const int AllowOfflineRenderers = 96;
    private const int ColorSize = 8;
    private const int AlphaSize = 11;
    private const int Profile = 99;
    private const int Version32Core = 0x3200;

    private readonly IntPtr context;
    private readonly IntPtr openGl;

    private CglContext(IntPtr context, IntPtr openGl)
    {
        this.context = context;
        this.openGl = openGl;
    }

    public static new CglContext? Open(out string? why)
    {
        why = null;

        if (!NativeLibrary.TryLoad(OpenGl, out var openGl))
        {
            why = "OpenGL.framework would not load.";
            return null;
        }

        var attributes = stackalloc int[]
        {
            Profile, Version32Core,
            ColorSize, 24,
            AlphaSize, 8,
            AllowOfflineRenderers,
            0,
        };

        IntPtr format;
        int count;

        if (CGLChoosePixelFormat(attributes, &format, &count) != 0 || format == 0)
        {
            why = "No renderer offers an OpenGL 3.2 core profile.";
            return null;
        }

        IntPtr context;
        var made = CGLCreateContext(format, 0, &context);
        _ = CGLDestroyPixelFormat(format);

        if (made != 0 || context == 0)
        {
            why = "The driver would not make an OpenGL context.";
            return null;
        }

        if (CGLSetCurrentContext(context) != 0)
        {
            _ = CGLDestroyContext(context);
            why = "The OpenGL context would not become current.";
            return null;
        }

        return new CglContext(context, openGl);
    }

    public override bool MakeCurrent() => CGLSetCurrentContext(context) == 0;

    /// <summary>Every entry point is an export of the framework itself.</summary>
    protected override IntPtr GetProcAddress(string name) =>
        NativeLibrary.TryGetExport(openGl, name, out var export) ? export : 0;

    public override void Dispose()
    {
        if (CGLGetCurrentContext() == context) _ = CGLSetCurrentContext(0);
        _ = CGLDestroyContext(context);
    }

    [LibraryImport(OpenGl)]
    private static partial int CGLChoosePixelFormat(int* attributes, IntPtr* format, int* count);

    [LibraryImport(OpenGl)]
    private static partial int CGLDestroyPixelFormat(IntPtr format);

    [LibraryImport(OpenGl)]
    private static partial int CGLCreateContext(IntPtr format, IntPtr share, IntPtr* context);

    [LibraryImport(OpenGl)]
    private static partial int CGLDestroyContext(IntPtr context);

    [LibraryImport(OpenGl)]
    private static partial int CGLSetCurrentContext(IntPtr context);

    [LibraryImport(OpenGl)]
    private static partial IntPtr CGLGetCurrentContext();
}
