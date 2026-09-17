using System.Runtime.InteropServices;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// The slice of the CoreAudio framework's hardware layer this plugin needs: what
/// output devices there are, what each is called, and the id that stays the same
/// across a reboot.
/// </summary>
/// <remarks>
/// Its own file because it is its own framework — Audio Toolbox plays, this one
/// lists. Every entry point is resolved lazily, so the assembly still loads on
/// Windows and Linux and answers "not supported".
/// </remarks>
internal static unsafe partial class AudioHardware
{
    /// <summary>The full framework path, for the reason <see cref="AudioToolbox"/> gives.</summary>
    private const string Library = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    /// <summary><c>kAudioObjectSystemObject</c> — the machine, which owns the device list.</summary>
    public const uint SystemObject = 1;

    // Four-character codes, in the order the bytes appear in the name.
    public const uint DevicesProperty = 0x6465_7623;   // 'dev#'
    public const uint StreamsProperty = 0x7374_6D23;   // 'stm#'
    public const uint NameProperty = 0x6C6E_616D;      // 'lnam'
    public const uint DeviceUidProperty = 0x7569_6420; // 'uid '

    public const uint GlobalScope = 0x676C_6F62; // 'glob'
    public const uint OutputScope = 0x6F75_7470; // 'outp'

    /// <summary><c>kAudioObjectPropertyElementMain</c>.</summary>
    public const uint MainElement = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [LibraryImport(Library, EntryPoint = "AudioObjectGetPropertyDataSize")]
    private static partial int DataSize(uint objectId, in PropertyAddress address, uint qualifierSize, IntPtr qualifier, out uint size);

    [LibraryImport(Library, EntryPoint = "AudioObjectGetPropertyData")]
    private static partial int Data(uint objectId, in PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size, void* data);

    /// <summary>
    /// Every device that can play, as the id this boot knows it by, the UID that
    /// outlives the boot, and the name the Sound settings show. A device whose UID
    /// cannot be read is left out, since it could not be chosen again next launch.
    /// </summary>
    public static IReadOnlyList<(uint Id, string Uid, string Name)> OutputDevices()
    {
        var all = new PropertyAddress { Selector = DevicesProperty, Scope = GlobalScope, Element = MainElement };

        if (DataSize(SystemObject, all, 0, IntPtr.Zero, out var size) != AudioToolbox.NoError || size == 0) return [];

        var ids = new uint[size / sizeof(uint)];

        fixed (uint* start = ids)
        {
            if (Data(SystemObject, all, 0, IntPtr.Zero, ref size, start) != AudioToolbox.NoError) return [];
        }

        var found = new List<(uint, string, string)>();

        foreach (var id in ids.AsSpan(0, (int)(size / sizeof(uint))))
        {
            if (!Plays(id)) continue;

            var uid = Text(id, DeviceUidProperty);

            if (uid.Length == 0) continue;

            var name = Text(id, NameProperty);

            found.Add((id, uid, name.Length == 0 ? uid : name));
        }

        return found;
    }

    /// <summary>Whether a device has any output streams — a microphone has none.</summary>
    private static bool Plays(uint device)
    {
        var streams = new PropertyAddress { Selector = StreamsProperty, Scope = OutputScope, Element = MainElement };

        return DataSize(device, streams, 0, IntPtr.Zero, out var size) == AudioToolbox.NoError && size > 0;
    }

    /// <summary>A string property, released once read, or nothing where it cannot be read.</summary>
    private static string Text(uint device, uint selector)
    {
        var address = new PropertyAddress { Selector = selector, Scope = GlobalScope, Element = MainElement };
        var text = IntPtr.Zero;
        var size = (uint)sizeof(IntPtr);

        if (Data(device, address, 0, IntPtr.Zero, ref size, &text) != AudioToolbox.NoError) return string.Empty;

        try
        {
            return CoreFoundation.Text(text);
        }
        finally
        {
            CoreFoundation.ReleaseIfAny(text);
        }
    }
}
