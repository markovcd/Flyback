using System.Runtime.InteropServices;

namespace Flyback.Editor.Composition;

/// <summary>
/// Whether the scroll macOS is delivering comes from a mouse wheel or a trackpad, which
/// Avalonia's wheel event does not say (ADR-0226).
/// </summary>
/// <remarks>
/// Avalonia raises the wheel event inside AppKit's <c>scrollWheel:</c>, so the application's
/// current event is that scroll. A trackpad or a Magic Mouse has precise scrolling deltas;
/// a wheel that clicks has none.
/// </remarks>
internal static partial class MacScroll
{
    private const nuint NSEventTypeScrollWheel = 22;

    private const string Runtime = "/usr/lib/libobjc.dylib";

    /// <summary>True for a mouse wheel, false for a trackpad, null when no scroll is being delivered.</summary>
    public static bool? IsMouse()
    {
        if (!OperatingSystem.IsMacOS()) return null;

        try
        {
            var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            var current = objc_msgSend(app, sel_registerName("currentEvent"));

            if (current == 0 || objc_msgSend_nuint(current, sel_registerName("type")) != NSEventTypeScrollWheel) return null;

            return objc_msgSend_byte(current, sel_registerName("hasPreciseScrollingDeltas")) == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nuint objc_msgSend_nuint(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte objc_msgSend_byte(IntPtr receiver, IntPtr selector);
}
