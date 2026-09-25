using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Flyback.App.Controls;

/// <summary>
/// Keeps the mouse pointer where a drag began, so the drag reads motion rather than position
/// and never runs into the edge of the screen.
/// </summary>
internal interface IPointerAnchor : IDisposable
{
    /// <summary>Puts the pointer back where it was taken. False when it did not go.</summary>
    bool Return();
}

/// <summary>Where a drag takes its <see cref="IPointerAnchor"/> from.</summary>
internal interface IPointerAnchors
{
    /// <summary>Anchors the pointer where it is now, or null to leave it free.</summary>
    IPointerAnchor? Take(Visual visual);
}

/// <summary>The platform's anchors.</summary>
internal sealed class PlatformAnchors : IPointerAnchors
{
    public static PlatformAnchors Instance { get; } = new();

    public IPointerAnchor? Take(Visual visual) => PointerAnchor.Take(visual);
}

/// <summary>
/// The platform's own anchor. Positions stay in the platform's screen coordinates end to end, so
/// no scaling or monitor layout has to be translated.
/// </summary>
internal static partial class PointerAnchor
{
    /// <summary>Anchors the pointer where it is now, or null where the window cannot move it.</summary>
    public static IPointerAnchor? Take(Visual visual)
    {
        var descriptor = TopLevel.GetTopLevel(visual)?.TryGetPlatformHandle()?.HandleDescriptor;

        try
        {
            return descriptor switch
            {
                "HWND" when OperatingSystem.IsWindows() => Win32.Take(),
                "NSWindow" when OperatingSystem.IsMacOS() => Mac.Take(),
                "XID" when OperatingSystem.IsLinux() => X11.Take(),
                _ => null,
            };
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private sealed partial class Win32(int x, int y) : IPointerAnchor
    {
        public static Win32? Take() => GetCursorPos(out var at) ? new Win32(at.X, at.Y) : null;

        public bool Return() => SetCursorPos(x, y) && GetCursorPos(out var at) && at.X == x && at.Y == y;

        public void Dispose()
        {
        }

        /// <summary>POINT.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out CursorPoint point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetCursorPos(int x, int y);
    }

    private sealed partial class Mac(CGPoint at) : IPointerAnchor
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        public static Mac Take() => new(Where());

        public bool Return()
        {
            if (CGWarpMouseCursorPosition(at) != 0) return false;

            // A warp otherwise mutes the mouse for a quarter of a second.
            _ = CGAssociateMouseAndMouseCursorPosition(1);

            var now = Where();

            return Math.Abs(now.X - at.X) < 0.5 && Math.Abs(now.Y - at.Y) < 0.5;
        }

        public void Dispose()
        {
        }

        private static CGPoint Where()
        {
            var e = CGEventCreate(IntPtr.Zero);

            try
            {
                return CGEventGetLocation(e);
            }
            finally
            {
                CFRelease(e);
            }
        }

        [LibraryImport(CoreGraphics)]
        private static partial IntPtr CGEventCreate(IntPtr source);

        [LibraryImport(CoreGraphics)]
        private static partial CGPoint CGEventGetLocation(IntPtr e);

        [LibraryImport(CoreGraphics)]
        private static partial int CGWarpMouseCursorPosition(CGPoint point);

        [LibraryImport(CoreGraphics)]
        private static partial int CGAssociateMouseAndMouseCursorPosition(int connected);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static partial void CFRelease(IntPtr cf);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    /// <summary>
    /// A display connection of its own, since Avalonia does not hand its own out. Under XWayland
    /// the warp is usually refused, which <see cref="Return"/> reports.
    /// </summary>
    private sealed partial class X11 : IPointerAnchor
    {
        private const string Library = "libX11.so.6";

        private readonly IntPtr display;
        private readonly IntPtr root;
        private readonly int x;
        private readonly int y;

        private X11(IntPtr display, IntPtr root, int x, int y)
        {
            this.display = display;
            this.root = root;
            this.x = x;
            this.y = y;
        }

        public static X11? Take()
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;

            var root = XDefaultRootWindow(display);

            if (Where(display, root) is { } at) return new X11(display, root, at.X, at.Y);

            _ = XCloseDisplay(display);

            return null;
        }

        public bool Return()
        {
            _ = XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, x, y);

            return Where(display, root) is { } at && at.X == x && at.Y == y;
        }

        public void Dispose() => _ = XCloseDisplay(display);

        private static (int X, int Y)? Where(IntPtr display, IntPtr root) =>
            XQueryPointer(display, root, out _, out _, out var x, out var y, out _, out _, out _) != 0 ? (x, y) : null;

        [LibraryImport(Library)]
        private static partial IntPtr XOpenDisplay(IntPtr displayName);

        [LibraryImport(Library)]
        private static partial int XCloseDisplay(IntPtr display);

        [LibraryImport(Library)]
        private static partial IntPtr XDefaultRootWindow(IntPtr display);

        [LibraryImport(Library)]
        private static partial int XQueryPointer(
            IntPtr display, IntPtr window,
            out IntPtr rootReturn, out IntPtr childReturn,
            out int rootX, out int rootY, out int windowX, out int windowY,
            out uint mask);

        [LibraryImport(Library)]
        private static partial int XWarpPointer(
            IntPtr display, IntPtr from, IntPtr to,
            int fromX, int fromY, uint fromWidth, uint fromHeight,
            int toX, int toY);
    }
}
