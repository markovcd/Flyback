using System.Runtime.InteropServices;
using System.Text;

namespace Flyback.Gpu;

/// <summary>
/// The Objective-C runtime, bound by hand: classes, selectors and <c>objc_msgSend</c>
/// cast to each shape a call needs, for the Metal API on macOS and iOS.
/// </summary>
/// <remarks>
/// Bound from the loaded library rather than imported, as <see cref="CglContext"/>
/// binds CGL, so nothing here loads on a platform without it. <c>objc_msgSend</c>
/// takes every argument shape on arm64 and on x86-64 as long as nothing returns a
/// struct, and no call here does.
/// </remarks>
internal static unsafe class ObjC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    private static readonly Dictionary<string, IntPtr> Selectors = [];
    private static readonly Dictionary<string, IntPtr> Classes = [];
    private static readonly Lock Gate = new();

    private static delegate* unmanaged<byte*, IntPtr> objcGetClass;
    private static delegate* unmanaged<byte*, IntPtr> selRegisterName;
    private static delegate* unmanaged<IntPtr> objcAutoreleasePoolPush;
    private static delegate* unmanaged<IntPtr, void> objcAutoreleasePoolPop;

    /// <summary>The address of <c>objc_msgSend</c>, cast at each call site to the call's shape.</summary>
    public static IntPtr MsgSend { get; private set; }

    /// <summary>Loads the runtime, or says why it would not.</summary>
    public static bool Load(out string? why)
    {
        lock (Gate)
        {
            if (MsgSend != 0)
            {
                why = null;
                return true;
            }

            if (!NativeLibrary.TryLoad(Runtime, out var runtime)
                || !NativeLibrary.TryGetExport(runtime, "objc_msgSend", out var send)
                || !NativeLibrary.TryGetExport(runtime, "objc_getClass", out var getClass)
                || !NativeLibrary.TryGetExport(runtime, "sel_registerName", out var register)
                || !NativeLibrary.TryGetExport(runtime, "objc_autoreleasePoolPush", out var push)
                || !NativeLibrary.TryGetExport(runtime, "objc_autoreleasePoolPop", out var pop))
            {
                why = "The Objective-C runtime would not load.";
                return false;
            }

            objcGetClass = (delegate* unmanaged<byte*, IntPtr>)getClass;
            selRegisterName = (delegate* unmanaged<byte*, IntPtr>)register;
            objcAutoreleasePoolPush = (delegate* unmanaged<IntPtr>)push;
            objcAutoreleasePoolPop = (delegate* unmanaged<IntPtr, void>)pop;
            MsgSend = send;

            why = null;
            return true;
        }
    }

    public static IntPtr Class(string name) => Cached(Classes, name, objcGetClass);

    public static IntPtr Sel(string name) => Cached(Selectors, name, selRegisterName);

    private static IntPtr Cached(Dictionary<string, IntPtr> cache, string name, delegate* unmanaged<byte*, IntPtr> look)
    {
        lock (Gate)
        {
            if (cache.TryGetValue(name, out var found)) return found;

            var bytes = Encoding.UTF8.GetBytes(name + '\0');

            fixed (byte* text = bytes)
                found = look(text);

            cache[name] = found;
            return found;
        }
    }

    /// <summary>Drains what the calls since <see cref="Push"/> autoreleased, which on a thread of its own nothing else would.</summary>
    public static IntPtr Push() => objcAutoreleasePoolPush();

    public static void Pop(IntPtr pool) => objcAutoreleasePoolPop(pool);

    public static IntPtr Send(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector));

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), argument);

    public static IntPtr Send(IntPtr receiver, string selector, nuint argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, IntPtr>)MsgSend)(receiver, Sel(selector), argument);

    public static void Call(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, void>)MsgSend)(receiver, Sel(selector));

    public static void Call(IntPtr receiver, string selector, IntPtr argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)MsgSend)(receiver, Sel(selector), argument);

    public static void Call(IntPtr receiver, string selector, nuint argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, void>)MsgSend)(receiver, Sel(selector), argument);

    public static void Call(IntPtr receiver, string selector, bool argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte, void>)MsgSend)(receiver, Sel(selector), argument ? (byte)1 : (byte)0);

    public static bool Is(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte>)MsgSend)(receiver, Sel(selector)) != 0;

    public static bool RespondsTo(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)MsgSend)(receiver, Sel("respondsToSelector:"), Sel(selector)) != 0;

    /// <summary>A new object of <paramref name="className"/>, owned by the caller.</summary>
    public static IntPtr New(string className) => Send(Send(Class(className), "alloc"), "init");

    public static void Release(IntPtr receiver)
    {
        if (receiver != 0) Call(receiver, "release");
    }

    /// <summary>An <c>NSString</c> of <paramref name="text"/>, owned by the caller.</summary>
    public static IntPtr String(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + '\0');

        fixed (byte* utf8 = bytes)
            return ((delegate* unmanaged<IntPtr, IntPtr, byte*, IntPtr>)MsgSend)(
                Send(Class("NSString"), "alloc"), Sel("initWithUTF8String:"), utf8);
    }

    /// <summary>What an <c>NSString</c> says, or null for none.</summary>
    public static string? Text(IntPtr nsString)
    {
        if (nsString == 0) return null;

        var utf8 = Send(nsString, "UTF8String");

        return utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
    }

    /// <summary>An <c>NSError</c>'s description, or <paramref name="otherwise"/> for none.</summary>
    public static string Describe(IntPtr error, string otherwise) =>
        Text(error == 0 ? 0 : Send(error, "localizedDescription")) ?? otherwise;
}
