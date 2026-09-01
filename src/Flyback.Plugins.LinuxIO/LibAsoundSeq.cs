using System.Runtime.InteropServices;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// The slice of the ALSA sequencer this plugin needs, and nothing else.
/// Hand-written for the same reason the sound bindings are: a list of entry
/// points does not justify a binding package, and <see cref="Flyback.Plugins"/>
/// having no dependencies is worth keeping true one level down as well.
/// </summary>
/// <remarks>
/// <para>
/// Longer than <c>LibAsound</c> and <c>WinMm</c> together, and none of it is
/// avoidable. The sequencer has no "how many devices are there" call: what is
/// plugged in is found by walking every client on the machine and every port on
/// every client, and each field of the two info blocks is read through an
/// accessor because those structs are opaque by design — which is the ABI
/// promise that makes calling them from here safe in the first place.
/// </para>
/// <para>
/// The one struct that is not opaque, <c>snd_seq_event_t</c>, is never read. It
/// arrives as a pointer and goes straight back into <see cref="Decode"/> —
/// libasound's own event-to-bytes converter — so the three bytes this plugin
/// cares about come out without anything here knowing where the union
/// boundaries fell.
/// </para>
/// <para>
/// Every entry point is resolved lazily by the runtime, on first call. Nothing
/// in this file runs while the plugin is merely being listed, which is what lets
/// the assembly load on a machine with no sound library at all and answer "not
/// supported" rather than failing to load.
/// </para>
/// </remarks>
internal static unsafe partial class LibAsoundSeq
{
    /// <summary>
    /// The same library the sound half of this plugin calls, and named there —
    /// see <see cref="LibAsound.Library"/>. Two SONAMEs written out separately
    /// are two that can come to disagree.
    /// </summary>
    private const string Library = LibAsound.Library;

    /// <summary>
    /// <c>default</c> is the sequencer every ALSA program opens. Unlike the PCM
    /// device of the same name it names no card and takes nothing exclusively —
    /// it is the kernel's own routing table, and hardware ports, other programs'
    /// ports and PipeWire's bridge all appear in it together.
    /// </summary>
    public const string DefaultSequencer = "default";

    /// <summary><c>SND_SEQ_OPEN_INPUT</c>. Nothing here ever sends a note.</summary>
    public const int InputStream = 2;

    public const int Blocking = 0;

    /// <summary>
    /// <c>SND_SEQ_NONBLOCK</c>. What makes <see cref="EventInput"/> answer
    /// "nothing yet" instead of parking the thread that asked — see
    /// <c>AlsaMidiPort</c> for why a reader that cannot be woken is a port that
    /// cannot be closed.
    /// </summary>
    public const int NonBlocking = 1;

    /// <summary>
    /// <c>SND_SEQ_PORT_CAP_READ | _SUBS_READ</c> — a port that produces notes
    /// and will let somebody else subscribe to them. Both bits, because the
    /// first without the second is a port only its own client may read.
    /// </summary>
    public const uint Readable = (1 << 0) | (1 << 5);

    /// <summary>
    /// <c>SND_SEQ_PORT_CAP_WRITE | _SUBS_WRITE</c> — what our own port is, since
    /// a subscription delivers *into* the subscriber.
    /// </summary>
    public const uint Writable = (1 << 1) | (1 << 6);

    /// <summary>
    /// <c>SND_SEQ_PORT_CAP_NO_EXPORT</c> — a port that says it is not for
    /// strangers. Offering one in a picker would be offering a failure.
    /// </summary>
    public const uint Private = 1 << 7;

    /// <summary><c>SND_SEQ_PORT_TYPE_MIDI_GENERIC</c>.</summary>
    public const uint GenericMidi = 1 << 1;

    /// <summary>
    /// <c>SND_SEQ_PORT_TYPE_APPLICATION</c> — what a program's port is, as
    /// against a card's. It is what makes this appear in <c>aconnect</c> under
    /// its own name rather than looking like a piece of hardware.
    /// </summary>
    public const uint Application = 1 << 20;

    /// <summary>
    /// Client 0, which is the sequencer describing itself — a timer and an
    /// announcement channel, neither of which is an instrument.
    /// </summary>
    public const int SystemClient = 0;

    /// <summary><c>-EAGAIN</c>: nothing has arrived. The ordinary answer, many times a second.</summary>
    public const int Nothing = -11;

    /// <summary>
    /// <c>-ENOSPC</c>: notes arrived faster than they were read and the queue
    /// was emptied. Something was lost, the sequencer is fine, and there is
    /// nothing to do about it but carry on.
    /// </summary>
    public const int Overrun = -28;

    [LibraryImport(Library, EntryPoint = "snd_seq_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(out IntPtr seq, string name, int streams, int mode);

    [LibraryImport(Library, EntryPoint = "snd_seq_close")]
    public static partial int Close(IntPtr seq);

    /// <summary>
    /// What this program is called in <c>aconnect</c> and in every other
    /// program's picker. Worth setting: the default is the process id.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_set_client_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetClientName(IntPtr seq, string name);

    /// <summary>Our own number, so the walk below can skip the ports we made.</summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_client_id")]
    public static partial int ClientId(IntPtr seq);

    /// <summary>
    /// Makes the port notes are delivered into, and returns its number.
    /// "Simple" is the whole port-info dance done for us, which is what it is
    /// for when the port wants no queue and no timestamps.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_create_simple_port", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int CreateSimplePort(IntPtr seq, string name, uint caps, uint type);

    /// <summary>
    /// Subscribes our port to a device's. This is the moment the keyboard starts
    /// playing us, and the only call in this file that changes anything on the
    /// machine.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_connect_from")]
    public static partial int ConnectFrom(IntPtr seq, int port, int sourceClient, int sourcePort);

    /// <summary>
    /// The next event, into a buffer the sequencer owns. Non-negative is an
    /// event; <see cref="Nothing"/> is an empty queue in non-blocking mode.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_event_input")]
    public static partial int EventInput(IntPtr seq, out IntPtr message);

    // ---- walking what is plugged in --------------------------------------------

    [LibraryImport(Library, EntryPoint = "snd_seq_client_info_sizeof")]
    public static partial nuint ClientInfoSize();

    [LibraryImport(Library, EntryPoint = "snd_seq_client_info_set_client")]
    public static partial void ClientInfoSetClient(byte* info, int client);

    [LibraryImport(Library, EntryPoint = "snd_seq_client_info_get_client")]
    public static partial int ClientInfoGetClient(byte* info);

    [LibraryImport(Library, EntryPoint = "snd_seq_client_info_get_name")]
    public static partial IntPtr ClientInfoGetName(byte* info);

    /// <summary>Advances the block to the next client. Negative when there are no more.</summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_query_next_client")]
    public static partial int QueryNextClient(IntPtr seq, byte* info);

    [LibraryImport(Library, EntryPoint = "snd_seq_port_info_sizeof")]
    public static partial nuint PortInfoSize();

    [LibraryImport(Library, EntryPoint = "snd_seq_port_info_set_client")]
    public static partial void PortInfoSetClient(byte* info, int client);

    [LibraryImport(Library, EntryPoint = "snd_seq_port_info_set_port")]
    public static partial void PortInfoSetPort(byte* info, int port);

    [LibraryImport(Library, EntryPoint = "snd_seq_port_info_get_port")]
    public static partial int PortInfoGetPort(byte* info);

    [LibraryImport(Library, EntryPoint = "snd_seq_port_info_get_name")]
    public static partial IntPtr PortInfoGetName(byte* info);

    [LibraryImport(Library, EntryPoint = "snd_seq_port_info_get_capability")]
    public static partial uint PortInfoGetCapability(byte* info);

    /// <summary>Advances the block to the next port of the client it was set to.</summary>
    [LibraryImport(Library, EntryPoint = "snd_seq_query_next_port")]
    public static partial int QueryNextPort(IntPtr seq, byte* info);

    // ---- events back into bytes ------------------------------------------------

    /// <summary>
    /// A converter between the sequencer's events and the bytes a MIDI cable
    /// carries. <paramref name="size"/> is the buffer it keeps for the other
    /// direction, which this plugin never uses.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_midi_event_new")]
    public static partial int NewDecoder(nuint size, out IntPtr decoder);

    [LibraryImport(Library, EntryPoint = "snd_midi_event_free")]
    public static partial void FreeDecoder(IntPtr decoder);

    /// <summary>
    /// Turns off running status, so every message decodes with its own status
    /// byte in front of it. Without this a run of notes on one channel comes out
    /// as two bytes each after the first, exactly as a cable carries it — which
    /// is a saving on a wire and a missing message here.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_midi_event_no_status")]
    public static partial void NoRunningStatus(IntPtr decoder, int on);

    /// <summary>
    /// One event as bytes. Returns how many were written, or negative for an
    /// event that is not a MIDI message at all — which most of the sequencer's
    /// own traffic is.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_midi_event_decode")]
    public static partial nint Decode(IntPtr decoder, byte* buffer, nint count, IntPtr message);

    /// <summary>What a <c>const char *</c> off an info block says, or nothing.</summary>
    public static string Text(IntPtr utf8) => Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
}
