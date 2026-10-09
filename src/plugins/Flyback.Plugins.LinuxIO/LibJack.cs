using System.Runtime.InteropServices;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// The slice of libjack this plugin needs, hand-written like <see cref="LibAsound"/>.
/// </summary>
internal static unsafe partial class LibJack
{
    /// <summary>The SONAME, which exists wherever the library does, unlike the development symlink.</summary>
    public const string Library = "libjack.so.0";

    public const int NoStartServer = 0x01;  // JackNoStartServer
    public const int ServerName = 0x04;     // JackServerName

    public const ulong PortIsInput = 0x1;   // JackPortIsInput
    public const ulong PortIsOutput = 0x2;  // JackPortIsOutput
    public const ulong PortIsPhysical = 0x4; // JackPortIsPhysical

    /// <summary>JACK's name for a mono 32-bit float stream, which is what every audio port carries.</summary>
    public const string AudioType = "32 bit float mono audio";

    /// <summary>
    /// Connects to the running server named <paramref name="server"/>, or returns null. The C
    /// function is variadic; with <see cref="ServerName"/> in <paramref name="options"/> its one
    /// extra argument is the server's name, which a fixed signature passes correctly.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "jack_client_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Open(string name, int options, out int status, string server);

    [LibraryImport(Library, EntryPoint = "jack_client_close")]
    public static partial int Close(IntPtr client);

    [LibraryImport(Library, EntryPoint = "jack_activate")]
    public static partial int Activate(IntPtr client);

    /// <summary>Returns only once the process callback is no longer running.</summary>
    [LibraryImport(Library, EntryPoint = "jack_deactivate")]
    public static partial int Deactivate(IntPtr client);

    [LibraryImport(Library, EntryPoint = "jack_get_sample_rate")]
    public static partial uint SampleRate(IntPtr client);

    [LibraryImport(Library, EntryPoint = "jack_get_buffer_size")]
    public static partial uint BufferSize(IntPtr client);

    [LibraryImport(Library, EntryPoint = "jack_set_process_callback")]
    public static partial int SetProcessCallback(IntPtr client, delegate* unmanaged[Cdecl]<uint, IntPtr, int> process, IntPtr arg);

    [LibraryImport(Library, EntryPoint = "jack_on_shutdown")]
    public static partial void OnShutdown(IntPtr client, delegate* unmanaged[Cdecl]<IntPtr, void> shutdown, IntPtr arg);

    [LibraryImport(Library, EntryPoint = "jack_port_register", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr RegisterPort(IntPtr client, string name, string type, ulong flags, ulong bufferSize);

    /// <summary>The port's buffer for the cycle in progress; valid only inside the process callback.</summary>
    [LibraryImport(Library, EntryPoint = "jack_port_get_buffer")]
    public static partial float* PortBuffer(IntPtr port, uint frames);

    [LibraryImport(Library, EntryPoint = "jack_port_name")]
    private static partial IntPtr PortNamePointer(IntPtr port);

    [LibraryImport(Library, EntryPoint = "jack_connect", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Connect(IntPtr client, string source, string destination);

    /// <summary>A null-terminated array of names the caller gives back with <see cref="Free"/>, or null.</summary>
    [LibraryImport(Library, EntryPoint = "jack_get_ports", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr* Ports(IntPtr client, string? namePattern, string? typePattern, ulong flags);

    [LibraryImport(Library, EntryPoint = "jack_free")]
    private static partial void Free(void* memory);

    public static string PortName(IntPtr port) => Marshal.PtrToStringUTF8(PortNamePointer(port)) ?? "";

    /// <summary>The names of the audio ports with every one of <paramref name="flags"/>, in the server's order.</summary>
    public static IReadOnlyList<string> AudioPorts(IntPtr client, ulong flags)
    {
        var found = Ports(client, null, AudioType, flags);

        if (found == null) return [];

        try
        {
            var names = new List<string>();

            for (var port = found; *port != IntPtr.Zero; port++)
                names.Add(Marshal.PtrToStringUTF8(*port) ?? "");

            return names;
        }
        finally
        {
            Free(found);
        }
    }

    /// <summary>Whether the link now exists; already existing is a yes.</summary>
    public static bool Link(IntPtr client, string source, string destination)
    {
        const int Exists = 17; // EEXIST

        var status = Connect(client, source, destination);

        return status is 0 or Exists;
    }

    /// <summary>Whether libjack is on this machine at all, asked the way <see cref="LibAsound.IsInstalled"/> is.</summary>
    public static bool IsInstalled
    {
        get
        {
            if (!NativeLibrary.TryLoad(Library, out var handle)) return false;

            NativeLibrary.Free(handle);
            return true;
        }
    }
}
