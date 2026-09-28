using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Flyback.Gpu;

/// <summary>
/// An EGL context with no surface: desktop OpenGL 3.2 core where the driver has
/// it and ES 3.0 where it does not. Mesa's surfaceless platform needs no display
/// server, which is what lets a container with no X in it draw on llvmpipe.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class EglContext : HeadlessContext
{
    private const int None = 0x3038;
    private const int Extensions = 0x3055;
    private const int SurfaceType = 0x3033;
    private const int PbufferBit = 0x0001;
    private const int RenderableType = 0x3040;
    private const int OpenGlBit = 0x0008;
    private const int OpenGlEs3Bit = 0x0040;
    private const int RedSize = 0x3024;
    private const int GreenSize = 0x3023;
    private const int BlueSize = 0x3022;
    private const int Width = 0x3057;
    private const int Height = 0x3056;
    private const int OpenGlApi = 0x30A2;
    private const int OpenGlEsApi = 0x30A0;
    private const int ContextMajorVersion = 0x3098;
    private const int ContextMinorVersion = 0x30FB;
    private const int ContextProfileMask = 0x30FD;
    private const int CoreProfile = 0x0001;
    private const int PlatformSurfaceless = 0x31DD;
    private const int PlatformDevice = 0x313F;

    private static readonly int[] Pixel = [Width, 1, Height, 1, None];

    private readonly Egl egl;
    private readonly IntPtr display;
    private readonly IntPtr surface;
    private readonly IntPtr context;
    private readonly IntPtr[] libraries;

    private EglContext(Egl egl, IntPtr display, IntPtr surface, IntPtr context, IntPtr[] libraries)
    {
        this.egl = egl;
        this.display = display;
        this.surface = surface;
        this.context = context;
        this.libraries = libraries;
    }

    public static new EglContext? Open(out string? why)
    {
        if (!NativeLibrary.TryLoad("libEGL.so.1", out var library))
        {
            why = "There is no libEGL.so.1.";
            return null;
        }

        var egl = new Egl(library);
        why = "EGL found no display.";

        foreach (var display in Displays(egl))
        {
            int major, minor;
            if (egl.Initialize(display, &major, &minor) == 0) continue;

            // Desktop first, which speaks the same GLSL as the preview on Windows.
            foreach (var (api, renderable, attributes) in (ReadOnlySpan<(int, int, int[])>)
                     [
                         (OpenGlApi, OpenGlBit, [ContextMajorVersion, 3, ContextMinorVersion, 2, ContextProfileMask, CoreProfile, None]),
                         (OpenGlEsApi, OpenGlEs3Bit, [ContextMajorVersion, 3, None]),
                     ])
            {
                if (egl.BindApi(api) == 0) continue;
                if (Config(egl, display, renderable) is not var (config, pbuffer)) continue;

                IntPtr context;
                fixed (int* list = attributes) context = egl.CreateContext(display, config, 0, list);
                if (context == 0) continue;

                // Surfaceless where the driver allows it, a pixel of pbuffer where not.
                var surface = IntPtr.Zero;

                if (egl.MakeCurrent(display, 0, 0, context) == 0 && pbuffer)
                {
                    fixed (int* size = Pixel) surface = egl.CreatePbufferSurface(display, config, size);

                    if (surface == 0 || egl.MakeCurrent(display, surface, surface, context) == 0)
                    {
                        if (surface != 0) egl.DestroySurface(display, surface);
                        egl.DestroyContext(display, context);
                        continue;
                    }
                }
                else if (egl.GetCurrentContext() != context)
                {
                    egl.DestroyContext(display, context);
                    continue;
                }

                why = null;
                return new EglContext(egl, display, surface, context, Libraries(api));
            }

            egl.Terminate(display);
            why = "EGL has no OpenGL 3.2 core or OpenGL ES 3.0 context.";
        }

        return null;
    }

    /// <summary>
    /// Mesa's surfaceless platform, then the first device EGL can enumerate (a
    /// headless NVIDIA machine), then whatever the default display is.
    /// </summary>
    private static List<IntPtr> Displays(Egl egl)
    {
        var displays = new List<IntPtr>();
        var extensions = egl.QueryString(0, Extensions) ?? string.Empty;
        var platform = (delegate* unmanaged<int, IntPtr, IntPtr*, IntPtr>)egl.GetProcAddress("eglGetPlatformDisplayEXT");

        if (platform != null && extensions.Contains("EGL_MESA_platform_surfaceless", StringComparison.Ordinal))
        {
            var display = platform(PlatformSurfaceless, 0, null);
            if (display != 0) displays.Add(display);
        }

        var query = (delegate* unmanaged<int, IntPtr*, int*, int>)egl.GetProcAddress("eglQueryDevicesEXT");

        if (platform != null && query != null && extensions.Contains("EGL_EXT_platform_device", StringComparison.Ordinal))
        {
            IntPtr device;
            int count;

            if (query(1, &device, &count) != 0 && count > 0)
            {
                var display = platform(PlatformDevice, device, null);
                if (display != 0) displays.Add(display);
            }
        }

        var fallback = egl.GetDisplay(0);
        if (fallback != 0) displays.Add(fallback);

        return displays;
    }

    /// <summary>A config that renders <paramref name="renderable"/>, and whether it can back a pbuffer.</summary>
    private static (IntPtr Config, bool Pbuffer)? Config(Egl egl, IntPtr display, int renderable)
    {
        foreach (var surfaces in (ReadOnlySpan<int>)[PbufferBit, 0])
        {
            int[] wanted =
            [
                SurfaceType, surfaces,
                RenderableType, renderable,
                RedSize, 8,
                GreenSize, 8,
                BlueSize, 8,
                None,
            ];

            IntPtr config;
            int count, chosen;

            fixed (int* list = wanted) chosen = egl.ChooseConfig(display, list, &config, 1, &count);

            if (chosen != 0 && count > 0) return (config, surfaces != 0);
        }

        return null;
    }

    /// <summary>Where the entry points live that <c>eglGetProcAddress</c> may not hand out.</summary>
    private static IntPtr[] Libraries(int api)
    {
        string[] names = api == OpenGlApi ? ["libOpenGL.so.0", "libGL.so.1"] : ["libGLESv2.so.2"];

        return [.. names.Select(name => NativeLibrary.TryLoad(name, out var handle) ? handle : 0).Where(handle => handle != 0)];
    }

    public override bool MakeCurrent() => egl.MakeCurrent(display, surface, surface, context) != 0;

    protected override IntPtr GetProcAddress(string name)
    {
        var address = egl.GetProcAddress(name);
        if (address != 0) return address;

        foreach (var library in libraries)
            if (NativeLibrary.TryGetExport(library, name, out var export))
                return export;

        return 0;
    }

    public override void Dispose()
    {
        egl.MakeCurrent(display, 0, 0, 0);
        egl.DestroyContext(display, context);
        if (surface != 0) egl.DestroySurface(display, surface);
        egl.Terminate(display);
    }

    /// <summary>The slice of EGL this needs, bound from the library by name.</summary>
    private sealed class Egl(IntPtr library)
    {
        private readonly delegate* unmanaged<byte*, IntPtr> getProcAddress =
            (delegate* unmanaged<byte*, IntPtr>)NativeLibrary.GetExport(library, "eglGetProcAddress");

        private readonly delegate* unmanaged<IntPtr, int, IntPtr> queryString =
            (delegate* unmanaged<IntPtr, int, IntPtr>)NativeLibrary.GetExport(library, "eglQueryString");

        private readonly delegate* unmanaged<IntPtr, IntPtr> getDisplay =
            (delegate* unmanaged<IntPtr, IntPtr>)NativeLibrary.GetExport(library, "eglGetDisplay");

        public readonly delegate* unmanaged<IntPtr, int*, int*, int> Initialize =
            (delegate* unmanaged<IntPtr, int*, int*, int>)NativeLibrary.GetExport(library, "eglInitialize");

        public readonly delegate* unmanaged<IntPtr, int> Terminate =
            (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(library, "eglTerminate");

        public readonly delegate* unmanaged<int, int> BindApi =
            (delegate* unmanaged<int, int>)NativeLibrary.GetExport(library, "eglBindAPI");

        public readonly delegate* unmanaged<IntPtr, int*, IntPtr*, int, int*, int> ChooseConfig =
            (delegate* unmanaged<IntPtr, int*, IntPtr*, int, int*, int>)NativeLibrary.GetExport(library, "eglChooseConfig");

        public readonly delegate* unmanaged<IntPtr, IntPtr, IntPtr, int*, IntPtr> CreateContext =
            (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int*, IntPtr>)NativeLibrary.GetExport(library, "eglCreateContext");

        public readonly delegate* unmanaged<IntPtr, IntPtr, int> DestroyContext =
            (delegate* unmanaged<IntPtr, IntPtr, int>)NativeLibrary.GetExport(library, "eglDestroyContext");

        public readonly delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr> CreatePbufferSurface =
            (delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr>)NativeLibrary.GetExport(library, "eglCreatePbufferSurface");

        public readonly delegate* unmanaged<IntPtr, IntPtr, int> DestroySurface =
            (delegate* unmanaged<IntPtr, IntPtr, int>)NativeLibrary.GetExport(library, "eglDestroySurface");

        public readonly delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, int> MakeCurrent =
            (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, int>)NativeLibrary.GetExport(library, "eglMakeCurrent");

        public readonly delegate* unmanaged<IntPtr> GetCurrentContext =
            (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(library, "eglGetCurrentContext");

        public IntPtr GetProcAddress(string name)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(name + '\0');

            fixed (byte* text = bytes) return getProcAddress(text);
        }

        public string? QueryString(IntPtr display, int name) => Marshal.PtrToStringAnsi(queryString(display, name));

        public IntPtr GetDisplay(IntPtr native) => getDisplay(native);
    }
}
