using System.Runtime.InteropServices;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// The slice of Windows' multimedia library this plugin needs, and nothing else.
/// Hand-written for the same reason the ALSA and CoreAudio bindings are: eight
/// entry points and one struct do not justify a binding package, and
/// <see cref="Flyback.Plugins"/> having no dependencies is worth keeping true one
/// level down as well.
/// </summary>
/// <remarks>
/// Every entry point is resolved lazily by the runtime, on first call. Nothing in
/// this file runs while the plugin is merely being listed, which is what lets the
/// assembly load on a machine that has no winmm at all and answer "not supported"
/// rather than failing to load.
/// </remarks>
internal static unsafe partial class WinMm
{
    private const string Library = "winmm.dll";

    /// <summary>Success. Every entry point here returns an <c>MMRESULT</c>.</summary>
    public const uint Ok = 0;

    /// <summary>
    /// <c>CALLBACK_FUNCTION</c> — deliver messages by calling us, rather than by
    /// posting to a window or a thread queue. The other two would each need a
    /// message loop of their own; this needs a function pointer.
    /// </summary>
    public const uint CallbackFunction = 0x0003_0000;

    /// <summary>
    /// <c>MIM_DATA</c> — a short message arrived. The only one of the seven this
    /// plugin acts on: the opens and closes are things it already knows about,
    /// and the errors are malformed messages there is nothing to do about.
    /// </summary>
    public const uint DataMessage = 0x3C3;

    /// <summary>
    /// The longest name a driver may report, counted in characters and including
    /// the terminator — <c>MAXPNAMELEN</c>, which has been 32 for thirty years.
    /// </summary>
    private const int MaxNameLength = 32;

    /// <summary>
    /// <c>MIDIINCAPSW</c>. The name is a fixed buffer rather than a marshalled
    /// string so the whole struct stays blittable, which is what lets
    /// <c>LibraryImport</c> pass it without generating a marshaller.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MidiInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        public fixed char Name[MaxNameLength];
        public uint Support;
    }

    [LibraryImport(Library, EntryPoint = "midiInGetNumDevs")]
    private static partial uint DeviceCount();

    [LibraryImport(Library, EntryPoint = "midiInGetDevCapsW")]
    private static partial uint DeviceCaps(nuint device, out MidiInCaps caps, uint size);

    /// <summary>
    /// Opens a device. <paramref name="callback"/> is called on a thread the
    /// driver owns, with <paramref name="instance"/> handed back untouched.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "midiInOpen")]
    public static partial uint Open(out IntPtr device, uint index, IntPtr callback, IntPtr instance, uint flags);

    /// <summary>Begins delivering. Nothing arrives before this, not even a note already held.</summary>
    [LibraryImport(Library, EntryPoint = "midiInStart")]
    public static partial uint Start(IntPtr device);

    [LibraryImport(Library, EntryPoint = "midiInStop")]
    public static partial uint Stop(IntPtr device);

    /// <summary>
    /// Hands back everything the driver is holding. Without it a close can be
    /// refused with <c>MIDIERR_STILLPLAYING</c> and the device stays ours until
    /// the process ends.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "midiInReset")]
    public static partial uint Reset(IntPtr device);

    [LibraryImport(Library, EntryPoint = "midiInClose")]
    public static partial uint Close(IntPtr device);

    [LibraryImport(Library, EntryPoint = "midiInGetErrorTextW")]
    private static partial uint ErrorText(uint error, char* text, uint length);

    /// <summary>
    /// Every input device the machine has, in the order winmm numbers them —
    /// which is also the order the ids handed out from it are in, and therefore
    /// what turns an id back into the number this file needs.
    /// </summary>
    /// <remarks>
    /// A device whose capabilities cannot be read is left out rather than named
    /// as something. It would not open either, and a picker offering it would be
    /// offering a failure.
    /// </remarks>
    public static IReadOnlyList<string> DeviceNames()
    {
        var names = new List<string>();
        var count = DeviceCount();

        for (nuint device = 0; device < count; device++)
        {
            if (DeviceCaps(device, out var caps, (uint)sizeof(MidiInCaps)) != Ok) continue;

            names.Add(NameOf(ref caps));
        }

        return names;
    }

    /// <summary>What winmm says went wrong, in its own words where it has any.</summary>
    public static string Describe(uint error)
    {
        // Long enough for anything winmm has to say; the call fails rather than
        // truncating if it is not, and the number is the fallback for that too.
        var text = stackalloc char[256];

        if (ErrorText(error, text, 256) == Ok)
        {
            var said = new ReadOnlySpan<char>(text, 256);
            var end = said.IndexOf('\0');

            if (end > 0) return new string(said[..end]);
        }

        return $"error {error}";
    }

    /// <summary>
    /// The device name out of its fixed buffer, cut at the terminator. Drivers
    /// have been known to fill the whole buffer without one, so the length is a
    /// bound and not a promise.
    /// </summary>
    private static string NameOf(ref MidiInCaps caps)
    {
        fixed (char* start = caps.Name)
        {
            var name = new ReadOnlySpan<char>(start, MaxNameLength);
            var end = name.IndexOf('\0');

            return new string(end < 0 ? name : name[..end]);
        }
    }
}
