using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// The slice of Apple's CoreMIDI this plugin needs, and nothing else. Hand-written
/// for the same reason the sound bindings are: nine entry points and one C
/// structure do not justify a binding package.
/// </summary>
/// <remarks>
/// The deprecated half of the framework, because the newer
/// <c>MIDIInputPortCreateWithProtocol</c> takes an Objective-C block rather than a
/// function pointer — a great deal of machinery to fabricate from C# to avoid a
/// warning this language never sees. The older call is what every program that
/// reads MIDI on a Mac still uses.
/// <para>
/// Every entry point is resolved lazily on first call, so nothing here runs while
/// the plugin is merely being listed — which is what lets the assembly load on
/// Windows and answer "not supported".
/// </para>
/// </remarks>
internal static unsafe partial class MidiServices
{
    /// <summary>
    /// The full framework path rather than a bare name: <c>dlopen</c> resolves
    /// frameworks by path, and the probing the runtime does for a plain name
    /// would not find this one.
    /// </summary>
    private const string Library = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";

    /// <summary>Success. Every entry point here returns an <c>OSStatus</c>.</summary>
    public const int NoError = 0;

    /// <summary>
    /// <c>timeStamp</c> and <c>length</c>, ahead of the bytes. Ten on either
    /// architecture: a 64-bit timestamp at nought and a 16-bit length after it,
    /// with nothing padded between them whichever way the structure is laid out.
    /// </summary>
    private const int PacketHeaderBytes = 8 + 2;

    /// <summary>
    /// Whether the packet structures are packed to four bytes rather than laid out
    /// naturally, which is what <c>MIDIServices.h</c> does on ARM and nowhere else.
    /// </summary>
    /// <remarks>
    /// It decides where the first packet sits after the count and where the next
    /// sits after the last byte of this one — both below, and both what the
    /// framework's own <c>MIDIPacketNext</c> compiles to on each side of that
    /// <c>#if</c>.
    /// </remarks>
    private static readonly bool Packed =
        RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm;

    /// <summary>
    /// Where <c>MIDIPacketList.packet[0]</c> begins. The count is four bytes and
    /// a packet opens with a 64-bit timestamp, so a natural layout pads to eight
    /// and a packed one does not.
    /// </summary>
    private static readonly int FirstPacketOffset = Packed ? 4 : 8;

    /// <summary>
    /// The framework, opened once and kept open — unlike the presence check the
    /// ALSA binding makes and then balances, because the two constants below are
    /// pointers into this and closing the handle they were read through would be
    /// saying they had gone.
    /// </summary>
    private static readonly IntPtr Framework =
        NativeLibrary.TryLoad(Library, out var framework) ? framework : IntPtr.Zero;

    /// <summary>
    /// <c>kMIDIPropertyDisplayName</c> — what Audio MIDI Setup shows, which for a
    /// keyboard with one port is the keyboard's name and for a multi-port
    /// interface is the device and the port together.
    /// </summary>
    private static readonly IntPtr DisplayNameProperty = Constant("kMIDIPropertyDisplayName");

    /// <summary>
    /// <c>kMIDIPropertyName</c>, which everything has where the one above is
    /// missing.
    /// </summary>
    private static readonly IntPtr NameProperty = Constant("kMIDIPropertyName");

    /// <summary>
    /// How many things on this machine will play us something. Asking takes no
    /// client and opens nothing: the framework answers out of the MIDI server's
    /// own list, which is the list Audio MIDI Setup draws.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "MIDIGetNumberOfSources")]
    public static partial nuint SourceCount();

    /// <summary>
    /// One source by its place in that list. The reference it hands back is a
    /// number the server made up and is not the same one after a reboot, which
    /// is why it is never what a patch stores.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "MIDIGetSource")]
    public static partial uint Source(nuint index);

    /// <summary>
    /// This program, as the MIDI server knows it. Everything else hangs off one of
    /// these, and disposing it takes the ports and connections with it.
    /// </summary>
    /// <remarks>
    /// The notification callback is null, which lets this be called from whichever
    /// thread happens to be rewiring the patch: notifications go to the run loop of
    /// the creating thread, and a client asking for none needs no run loop. Reading
    /// is called back on a thread of the server's own.
    /// </remarks>
    [LibraryImport(Library, EntryPoint = "MIDIClientCreate")]
    public static partial int CreateClient(IntPtr name, IntPtr notify, IntPtr context, out uint client);

    [LibraryImport(Library, EntryPoint = "MIDIClientDispose")]
    public static partial int DisposeClient(uint client);

    /// <summary>
    /// Somewhere for notes to arrive. <paramref name="read"/> is called on a
    /// thread the server owns, with <paramref name="context"/> handed back
    /// untouched.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "MIDIInputPortCreate")]
    public static partial int CreateInputPort(uint client, IntPtr name, IntPtr read, IntPtr context, out uint port);

    [LibraryImport(Library, EntryPoint = "MIDIPortDispose")]
    public static partial int DisposePort(uint port);

    /// <summary>
    /// Points a port at a source. This is the moment the keyboard starts playing
    /// us, and the only call in this file that changes anything on the machine.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "MIDIPortConnectSource")]
    public static partial int ConnectSource(uint port, uint source, IntPtr context);

    [LibraryImport(Library, EntryPoint = "MIDIPortDisconnectSource")]
    public static partial int DisconnectSource(uint port, uint source);

    /// <summary>
    /// What a device is called, or nothing where it will not say. The display name
    /// first because it is the one a person will recognise — CoreMIDI has already
    /// joined the device's name to the port's. The plainer name is the fallback for
    /// a virtual source that never got a display name.
    /// </summary>
    public static string NameOf(uint endpoint)
    {
        var display = TextProperty(endpoint, DisplayNameProperty);

        return display.Length > 0 ? display : TextProperty(endpoint, NameProperty);
    }

    // ---- reading a packet list -------------------------------------------------

    /// <summary>How many packets arrived in the one call.</summary>
    public static uint PacketCount(byte* list) => Unsafe.ReadUnaligned<uint>(list);

    public static byte* FirstPacket(byte* list) => list + FirstPacketOffset;

    /// <summary>
    /// How many bytes of MIDI this packet holds. Read unaligned because on a
    /// natural layout every packet after the first begins wherever the last one
    /// ended, which is any offset at all.
    /// </summary>
    public static int PacketLength(byte* packet) => Unsafe.ReadUnaligned<ushort>(packet + 8);

    public static byte* PacketData(byte* packet) => packet + PacketHeaderBytes;

    /// <summary>
    /// The packet after this one — <c>MIDIPacketNext</c>, which is the byte past the
    /// last one, rounded up to four where the structures are packed.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from a size, because the size lies: the
    /// structure declares room for 256 bytes and a packet occupies only what it
    /// carries.
    /// </remarks>
    public static byte* NextPacket(byte* packet)
    {
        var end = packet + PacketHeaderBytes + PacketLength(packet);

        return Packed ? (byte*)(((nuint)end + 3) & ~(nuint)3) : end;
    }

    /// <summary>
    /// What CoreMIDI says went wrong, in words where there are any to be had. The
    /// framework has no error-text call, so this is its header read back; only the
    /// ones somebody could act on are named.
    /// </summary>
    public static string Describe(int status) => status switch
    {
        -10830 => "the MIDI client is not valid",
        -10831 => "the MIDI port is not valid",
        -10832 => "that is not something that plays notes",
        -10833 => "nothing is connected to that port",
        -10834 or -10842 => "the device is not there any more",
        -10837 => "there is no MIDI setup on this machine",
        -10839 => "the MIDI server would not start",
        -10841 => "asked from the wrong thread",
        -10844 => "macOS would not permit it",
        _ => $"error {status}",
    };

    /// <summary>
    /// One string property of one object, let go of before it is returned — the
    /// framework hands back a string that is ours to release.
    /// </summary>
    private static string TextProperty(uint endpoint, IntPtr property)
    {
        // A constant that could not be read off the framework, which would make
        // the call below a null dereference inside somebody else's code.
        if (property == IntPtr.Zero) return string.Empty;

        if (StringProperty(endpoint, property, out var text) != NoError) return string.Empty;

        try
        {
            return CoreFoundation.Text(text);
        }
        finally
        {
            CoreFoundation.ReleaseIfAny(text);
        }
    }

    [LibraryImport(Library, EntryPoint = "MIDIObjectGetStringProperty")]
    private static partial int StringProperty(uint obj, IntPtr property, out IntPtr text);

    /// <summary>
    /// One of the framework's own string constants, by symbol.
    /// </summary>
    /// <remarks>
    /// The keys are <c>CFStringRef</c> globals — <c>kMIDIPropertyDisplayName</c> is
    /// the framework's own object rather than the characters it spells — so they are
    /// read out of it: a fact taken from the library cannot drift away from it.
    /// </remarks>
    private static IntPtr Constant(string symbol) =>
        Framework != IntPtr.Zero && NativeLibrary.TryGetExport(Framework, symbol, out var address)
            ? *(IntPtr*)address
            : IntPtr.Zero;
}
