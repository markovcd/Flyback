using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Flyback.App;

/// <summary>
/// Marks the window as wanting a look, on whichever platform is asked.
/// </summary>
/// <remarks>
/// Stealing focus is not on offer, on any platform, and rightly not — a synth
/// that grabbed the keyboard mid-take because a dialog wanted an answer would
/// be worse than the dialog waiting quietly. What every window manager takes
/// instead is a blink to be noticed and switched to on purpose: the taskbar
/// button on Windows, the dock icon on macOS, the window-list entry on Linux.
/// Three different names for the same request, so it is asked once, here,
/// rather than at every place a dialog can appear.
/// </remarks>
internal static partial class Attention
{
    /// <summary>Asks for that blink, if the window is not the one being typed into.</summary>
    public static void Request(Window window)
    {
        if (window.IsActive) return;

        if (OperatingSystem.IsWindows()) RequestWindows(window);
        else if (OperatingSystem.IsMacOS()) RequestMacOS();
        else if (OperatingSystem.IsLinux()) RequestLinux(window);
    }

    /// <summary>
    /// Lets go of a blink still going. Windows and macOS already stop on their
    /// own the moment the window is activated — that is what "until noticed"
    /// means to both of them — but a bare ICCCM urgency bit is the window
    /// manager's to clear, and not every one of them does.
    /// </summary>
    public static void Clear(Window window)
    {
        if (OperatingSystem.IsLinux()) ClearLinux(window);
    }

    #region Windows — the taskbar button

    private const uint FLASHW_TRAY = 0x00000002;

    // Keeps the button flashing until this window is brought to the
    // foreground, rather than a fixed number of blinks nobody may be there to see.
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    private static void RequestWindows(Window window)
    {
        if (window.TryGetPlatformHandle() is not { } handle) return;

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = handle.Handle,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
        };

        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FLASHWINFO pwfi);

    #endregion

    #region macOS — the dock icon

    // NSCriticalRequest: bounces continuously until the app is activated,
    // rather than NSInformationalRequest's single bounce nobody may see.
    private const nint NSCriticalRequest = 0;

    private static void RequestMacOS()
    {
        var nsApplication = objc_getClass("NSApplication");
        var app = objc_msgSend(nsApplication, sel_registerName("sharedApplication"));

        objc_msgSend(app, sel_registerName("requestUserAttention:"), NSCriticalRequest);
    }

    [LibraryImport("/usr/lib/libobjc.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport("/usr/lib/libobjc.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, nint arg1);

    #endregion

    #region Linux — the ICCCM urgency hint

    private const long XUrgencyHint = 1 << 8;

    private static void RequestLinux(Window window) => EditUrgency(window, urgent: true);

    private static void ClearLinux(Window window) => EditUrgency(window, urgent: false);

    /// <summary>
    /// A connection of its own rather than Avalonia's, because Avalonia does not
    /// hand its X11 display out — opening a second one to the same server for a
    /// single property write is what every other program reaching for Xlib from
    /// outside its own event loop does too.
    /// </summary>
    private static void EditUrgency(Window window, bool urgent)
    {
        if (window.TryGetPlatformHandle() is not { } handle) return;

        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) return;

        try
        {
            var hints = XGetWMHints(display, handle.Handle);

            // Setting the bit needs somewhere to set it even if the window
            // manager never gave this window hints of its own; clearing a bit
            // that was never there has nothing to do.
            if (hints == IntPtr.Zero)
            {
                if (!urgent) return;

                hints = XAllocWMHints();
                if (hints == IntPtr.Zero) return;
            }

            // flags is the struct's first field, and the only one this reaches
            // for — the rest is icons and an initial state this has no opinion on.
            var flags = Marshal.ReadInt64(hints);
            Marshal.WriteInt64(hints, urgent ? flags | XUrgencyHint : flags & ~XUrgencyHint);

            _ = XSetWMHints(display, handle.Handle, hints);
            _ = XFree(hints);
            _ = XFlush(display);
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    [LibraryImport("libX11.so.6")]
    private static partial IntPtr XOpenDisplay(IntPtr displayName);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6")]
    private static partial IntPtr XGetWMHints(IntPtr display, IntPtr window);

    [LibraryImport("libX11.so.6")]
    private static partial IntPtr XAllocWMHints();

    [LibraryImport("libX11.so.6")]
    private static partial int XSetWMHints(IntPtr display, IntPtr window, IntPtr wmhints);

    [LibraryImport("libX11.so.6")]
    private static partial int XFree(IntPtr data);

    [LibraryImport("libX11.so.6")]
    private static partial int XFlush(IntPtr display);

    #endregion
}
