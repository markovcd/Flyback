using System.Runtime.InteropServices;

namespace Flyback.Plugins.CoreMidi;

/// <summary>
/// Strings, in the only currency CoreMIDI accepts them in. Every name that
/// crosses into the framework — what this program is called, what its port is
/// called — and every name that comes back out of it is a
/// <c>CFStringRef</c>, so three entry points of Core Foundation are the price of
/// asking a keyboard what it is called.
/// </summary>
/// <remarks>
/// <para>
/// Its own file rather than sitting among the MIDI calls, because it is a
/// different framework with a different rule attached: Core Foundation objects
/// are counted, and every one made here or handed back by a <c>Get</c> with
/// <c>Copy</c> semantics has to be released by whoever took it. Keeping that in
/// one place is what makes it possible to see that it is obeyed.
/// </para>
/// <para>
/// Every entry point is resolved lazily by the runtime, on first call. Nothing
/// in this file runs while the plugin is merely being listed, which is what lets
/// the assembly load on Windows and answer "not supported" rather than failing
/// to load at all.
/// </para>
/// </remarks>
internal static unsafe partial class CoreFoundation
{
    /// <summary>
    /// The full framework path rather than a bare name: <c>dlopen</c> resolves
    /// frameworks by path, and the probing the runtime does for a plain name
    /// would not find this one.
    /// </summary>
    private const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary><c>kCFStringEncodingUTF8</c>, in both directions.</summary>
    private const uint Utf8 = 0x0800_0100;

    /// <summary>
    /// Room for any name a device has. Apple's own limit on the property is far
    /// under this, and a name that somehow did not fit comes back as nothing
    /// rather than as half of itself — which <see cref="Midi.MidiPorts.Named"/>
    /// turns into "MIDI device", and a picker that says that is better than one
    /// that says nothing at all.
    /// </summary>
    private const int NameBytes = 512;

    /// <summary>
    /// A string the framework can hold, made from one of ours. The caller owns
    /// it and must <see cref="Release"/> it — the calls it is handed to copy
    /// what they need.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr NewText(IntPtr allocator, string text, uint encoding);

    /// <summary>The allocator argument every <c>Create</c> takes, and the default one.</summary>
    public static IntPtr NewText(string text) => NewText(IntPtr.Zero, text, Utf8);

    [LibraryImport(Library, EntryPoint = "CFRelease")]
    public static partial void Release(IntPtr reference);

    /// <summary>Releases one, if there is one. Core Foundation is not kind about nulls.</summary>
    public static void ReleaseIfAny(IntPtr reference)
    {
        if (reference != IntPtr.Zero) Release(reference);
    }

    /// <summary>
    /// What a <c>CFStringRef</c> says, or nothing where it will not fit or will
    /// not convert. Reading a name is never worth failing over: the device is
    /// still there and still playable, and it is the id rather than the display
    /// name that a patch stores.
    /// </summary>
    public static string Text(IntPtr text)
    {
        if (text == IntPtr.Zero) return string.Empty;

        var bytes = stackalloc byte[NameBytes];

        return ReadText(text, bytes, NameBytes, Utf8)
            ? Marshal.PtrToStringUTF8((IntPtr)bytes) ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Fills a buffer with the string as bytes. False for a buffer too small,
    /// which is the only failure that can happen to a device name.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "CFStringGetCString")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool ReadText(IntPtr text, byte* buffer, nint size, uint encoding);
}
