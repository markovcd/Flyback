using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// Names follow the native API they bind.
// ReSharper disable InconsistentNaming

namespace Flyback.Gpu;

/// <summary>
/// A CGL context with no drawable: OpenGL 3.2 core, on the GPU where there is one
/// and on Apple's software renderer where there is not. Every frame goes to a
/// framebuffer of the renderer's own.
/// </summary>
/// <remarks>
/// CGL is bound from the loaded framework rather than imported, so an iOS build, which
/// links every framework an import names and has no OpenGL.framework, links.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class CglContext : HeadlessContext
{
    private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    private const int AllowOfflineRenderers = 96;
    private const int ColorSize = 8;
    private const int AlphaSize = 11;
    private const int Profile = 99;
    private const int Version32Core = 0x3200;

    private static delegate* unmanaged<int*, IntPtr*, int*, int> CGLChoosePixelFormat;
    private static delegate* unmanaged<IntPtr, int> CGLDestroyPixelFormat;
    private static delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int> CGLCreateContext;
    private static delegate* unmanaged<IntPtr, int> CGLDestroyContext;
    private static delegate* unmanaged<IntPtr, int> CGLSetCurrentContext;
    private static delegate* unmanaged<IntPtr> CGLGetCurrentContext;

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

        if (!Bind(openGl))
        {
            why = "OpenGL.framework has no CGL.";
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

    private static bool Bind(IntPtr openGl)
    {
        if (!NativeLibrary.TryGetExport(openGl, nameof(CGLChoosePixelFormat), out var choose)
            || !NativeLibrary.TryGetExport(openGl, nameof(CGLDestroyPixelFormat), out var destroyFormat)
            || !NativeLibrary.TryGetExport(openGl, nameof(CGLCreateContext), out var create)
            || !NativeLibrary.TryGetExport(openGl, nameof(CGLDestroyContext), out var destroy)
            || !NativeLibrary.TryGetExport(openGl, nameof(CGLSetCurrentContext), out var setCurrent)
            || !NativeLibrary.TryGetExport(openGl, nameof(CGLGetCurrentContext), out var getCurrent))
            return false;

        CGLChoosePixelFormat = (delegate* unmanaged<int*, IntPtr*, int*, int>)choose;
        CGLDestroyPixelFormat = (delegate* unmanaged<IntPtr, int>)destroyFormat;
        CGLCreateContext = (delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)create;
        CGLDestroyContext = (delegate* unmanaged<IntPtr, int>)destroy;
        CGLSetCurrentContext = (delegate* unmanaged<IntPtr, int>)setCurrent;
        CGLGetCurrentContext = (delegate* unmanaged<IntPtr>)getCurrent;
        return true;
    }
}
