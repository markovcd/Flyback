using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Flyback.Gpu;

/// <summary>
/// A core-profile WGL context on a window nobody ever shows. WGL has no context
/// without a window, but the window is only there to lend a pixel format: nothing
/// is drawn to it, and every frame goes to a framebuffer of the renderer's own.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WglContext : HeadlessContext
{
    private const string User32 = "user32.dll";
    private const string Gdi32 = "gdi32.dll";
    private const string OpenGl32 = "opengl32.dll";
    private const string Kernel32 = "kernel32.dll";

    private const string ClassName = "FlybackHeadlessGl";

    private const uint OwnDc = 0x0020;
    private const uint Popup = 0x80000000;

    private const uint DrawToWindow = 0x0004;
    private const uint SupportOpenGl = 0x0020;
    private const uint DoubleBuffer = 0x0001;

    private const int ContextMajorVersion = 0x2091;
    private const int ContextMinorVersion = 0x2092;
    private const int ContextProfileMask = 0x9126;
    private const int CoreProfile = 0x0001;

    private static readonly Lock Registering = new();
    private static bool registered;

    private readonly IntPtr window;
    private readonly IntPtr device;
    private readonly IntPtr context;
    private readonly IntPtr openGl;

    private WglContext(IntPtr window, IntPtr device, IntPtr context, IntPtr openGl)
    {
        this.window = window;
        this.device = device;
        this.context = context;
        this.openGl = openGl;
    }

    public static new WglContext? Open(out string? why)
    {
        why = null;

        if (!NativeLibrary.TryLoad(OpenGl32, out var openGl))
        {
            why = "opengl32.dll would not load.";
            return null;
        }

        if (!Register())
        {
            why = "The window class for a headless context would not register.";
            return null;
        }

        var window = CreateWindowEx(0, ClassName, ClassName, Popup, 0, 0, 1, 1, 0, 0, GetModuleHandle(0), 0);

        if (window == 0)
        {
            why = "A hidden window would not open.";
            return null;
        }

        var device = GetDC(window);
        var format = new PixelFormatDescriptor
        {
            Size = (ushort)sizeof(PixelFormatDescriptor),
            Version = 1,
            Flags = DrawToWindow | SupportOpenGl | DoubleBuffer,
            ColorBits = 32,
            AlphaBits = 8,
        };

        var chosen = ChoosePixelFormat(device, &format);

        if (chosen == 0 || SetPixelFormat(device, chosen, &format) == 0)
        {
            Close(window, device, 0);
            why = "The window offers no pixel format OpenGL draws with.";
            return null;
        }

        // The legacy context exists only to be asked for the call that makes a
        // core one: a context has to be current before WGL names any extension.
        var legacy = wglCreateContext(device);

        if (legacy == 0 || wglMakeCurrent(device, legacy) == 0)
        {
            if (legacy != 0) _ = wglDeleteContext(legacy);
            Close(window, device, 0);
            why = "The driver would not make an OpenGL context.";
            return null;
        }

        var create = (delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr>)wglGetProcAddress("wglCreateContextAttribsARB");
        var context = IntPtr.Zero;

        if (create != null)
        {
            var attributes = stackalloc int[]
            {
                ContextMajorVersion, 3,
                ContextMinorVersion, 2,
                ContextProfileMask, CoreProfile,
                0,
            };

            context = create(device, 0, attributes);
        }

        _ = wglMakeCurrent(0, 0);
        _ = wglDeleteContext(legacy);

        if (context == 0 || wglMakeCurrent(device, context) == 0)
        {
            Close(window, device, context);
            why = "The driver has no OpenGL 3.2 core profile.";
            return null;
        }

        return new WglContext(window, device, context, openGl);
    }

    public override bool MakeCurrent() => wglMakeCurrent(device, context) != 0;

    /// <remarks>
    /// <c>wglGetProcAddress</c> knows only what came after OpenGL 1.1, and answers
    /// 1, 2, 3 or -1 as well as 0 for a name it does not; the rest are exports of
    /// opengl32.dll itself.
    /// </remarks>
    protected override IntPtr GetProcAddress(string name)
    {
        var address = wglGetProcAddress(name);

        if (address is 0 or 1 or 2 or 3 or -1)
            return NativeLibrary.TryGetExport(openGl, name, out var export) ? export : 0;

        return address;
    }

    public override void Dispose() => Close(window, device, context);

    private static void Close(IntPtr window, IntPtr device, IntPtr context)
    {
        if (context != 0)
        {
            _ = wglMakeCurrent(0, 0);
            _ = wglDeleteContext(context);
        }

        if (device != 0) _ = ReleaseDC(window, device);
        if (window != 0) _ = DestroyWindow(window);
    }

    /// <summary>
    /// Once a process: a class of its own, because a context wants a device
    /// context the window keeps rather than one lent from a shared cache.
    /// </summary>
    private static bool Register()
    {
        lock (Registering)
        {
            if (registered) return true;

            var procedure = NativeLibrary.GetExport(NativeLibrary.Load(User32), "DefWindowProcW");

            fixed (char* name = ClassName)
            {
                var type = new WindowClass
                {
                    Size = (uint)sizeof(WindowClass),
                    Style = OwnDc,
                    Procedure = procedure,
                    Instance = GetModuleHandle(0),
                    Name = (IntPtr)name,
                };

                registered = RegisterClassEx(&type) != 0;
            }

            return registered;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr Menu;
        public IntPtr Name;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size;
        public ushort Version;
        public uint Flags;
        public byte PixelType;
        public byte ColorBits;
        public byte RedBits;
        public byte RedShift;
        public byte GreenBits;
        public byte GreenShift;
        public byte BlueBits;
        public byte BlueShift;
        public byte AlphaBits;
        public byte AlphaShift;
        public byte AccumBits;
        public byte AccumRedBits;
        public byte AccumGreenBits;
        public byte AccumBlueBits;
        public byte AccumAlphaBits;
        public byte DepthBits;
        public byte StencilBits;
        public byte AuxBuffers;
        public byte LayerType;
        public byte Reserved;
        public uint LayerMask;
        public uint VisibleMask;
        public uint DamageMask;
    }

    [LibraryImport(Kernel32, EntryPoint = "GetModuleHandleW")]
    private static partial IntPtr GetModuleHandle(IntPtr name);

    [LibraryImport(User32, EntryPoint = "RegisterClassExW")]
    private static partial ushort RegisterClassEx(WindowClass* type);

    [LibraryImport(User32, EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [LibraryImport(User32)]
    private static partial int DestroyWindow(IntPtr window);

    [LibraryImport(User32)]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport(User32)]
    private static partial int ReleaseDC(IntPtr window, IntPtr device);

    [LibraryImport(Gdi32)]
    private static partial int ChoosePixelFormat(IntPtr device, PixelFormatDescriptor* format);

    [LibraryImport(Gdi32)]
    private static partial int SetPixelFormat(IntPtr device, int format, PixelFormatDescriptor* descriptor);

    [LibraryImport(OpenGl32)]
    private static partial IntPtr wglCreateContext(IntPtr device);

    [LibraryImport(OpenGl32)]
    private static partial int wglDeleteContext(IntPtr context);

    [LibraryImport(OpenGl32)]
    private static partial int wglMakeCurrent(IntPtr device, IntPtr context);

    [LibraryImport(OpenGl32, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr wglGetProcAddress(string name);
}
