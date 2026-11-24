using System.Runtime.InteropServices;

namespace Flyback.Plugins.Ndi;

/// <summary>
/// The NDI runtime the user installed, found where NDI's installers put it and nowhere
/// else, and loaded once for the process.
/// </summary>
/// <remarks>
/// Flyback ships none of NDI's files: its license keeps them out of the repository and
/// the release, so the runtime is the one NDI Tools or the NDI SDK installed. The search
/// path is never consulted, so a library of that name dropped anywhere else is not
/// loaded into the process.
/// </remarks>
internal static unsafe class NdiRuntime
{
    /// <summary>The variable NDI's installers set to the runtime's folder.</summary>
    public const string Folder = "NDI_RUNTIME_DIR_V6";

    /// <summary>Where to get the runtime, said whenever it is missing.</summary>
    public const string Download = "https://ndi.video/tools/";

    private static readonly Lock Gate = new();

    private static Api? loaded;

    /// <summary>Where the runtime can be, in the order it is looked for, on this machine.</summary>
    public static IReadOnlyList<string> Candidates(string? folder, OSPlatform platform, Architecture architecture)
    {
        var name = LibraryName(platform, architecture);
        if (name is null) return [];

        var places = new List<string>();

        if (Absolute(folder, platform)) places.Add(Join(folder!, name, platform));

        if (platform == OSPlatform.OSX)
        {
            places.Add("/usr/local/lib/libndi.dylib");
            places.Add("/Library/NDI SDK for Apple/lib/macOS/libndi.dylib");
        }
        else if (platform == OSPlatform.Linux)
        {
            var triplet = architecture == Architecture.Arm64 ? "aarch64-linux-gnu" : "x86_64-linux-gnu";

            places.Add($"/usr/lib/{triplet}/{name}");
            places.Add($"/usr/lib/{name}");
            places.Add($"/usr/local/lib/{name}");
        }

        return places;
    }

    /// <summary>Whether <paramref name="folder"/> names a folder from the root on <paramref name="platform"/>, never one relative to wherever Flyback was started.</summary>
    private static bool Absolute(string? folder, OSPlatform platform) =>
        !string.IsNullOrWhiteSpace(folder) && (platform == OSPlatform.Windows
            ? (folder.Length > 2 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && folder[2] is '\\' or '/') || folder.StartsWith(@"\\", StringComparison.Ordinal)
            : folder[0] == '/');

    private static string Join(string folder, string name, OSPlatform platform)
    {
        var separator = platform == OSPlatform.Windows ? '\\' : '/';

        return folder.TrimEnd('\\', '/') + separator + name;
    }

    /// <summary>The runtime's file name for a platform, or null where NDI has none.</summary>
    public static string? LibraryName(OSPlatform platform, Architecture architecture)
    {
        if (platform == OSPlatform.Windows)
        {
            return architecture switch
            {
                Architecture.X64 => "Processing.NDI.Lib.x64.dll",
                Architecture.X86 => "Processing.NDI.Lib.x86.dll",
                _ => null,
            };
        }

        if (platform == OSPlatform.OSX) return "libndi.dylib";

        if (platform == OSPlatform.Linux) return "libndi.so.6";

        return null;
    }

    /// <summary>The runtime on this machine, or null where none is installed. Opens nothing.</summary>
    public static string? Find() =>
        Candidates(Environment.GetEnvironmentVariable(Folder), Here, RuntimeInformation.ProcessArchitecture).FirstOrDefault(File.Exists);

    private static OSPlatform Here =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsLinux() ? OSPlatform.Linux
        : OSPlatform.FreeBSD;

    /// <summary>The runtime's entry points, loading and starting it the first time.</summary>
    /// <exception cref="InvalidOperationException">No runtime is installed, or the one that is cannot be used.</exception>
    public static Api Open()
    {
        lock (Gate)
        {
            if (loaded is { } api) return api;

            var path = Find() ?? throw new InvalidOperationException($"The NDI runtime is not installed. Install NDI Tools from {Download}.");

            if (!NativeLibrary.TryLoad(path, out var library))
                throw new InvalidOperationException($"The NDI runtime at {path} would not load.");

            try
            {
                var opened = new Api(library);

                if (!opened.Initialize()) throw new InvalidOperationException("The NDI runtime would not start: this processor may be one it does not support.");

                return loaded = opened;
            }
            catch (EntryPointNotFoundException ex)
            {
                NativeLibrary.Free(library);
                throw new InvalidOperationException($"The NDI runtime at {path} is not a version this plugin can use ({ex.Message}). Install NDI 6.", ex);
            }
        }
    }

    /// <summary>The functions this plugin calls, looked up by name in the runtime it loaded.</summary>
    internal sealed class Api(IntPtr library)
    {
        private readonly delegate* unmanaged[Cdecl]<byte> initialize = (delegate* unmanaged[Cdecl]<byte>)Export(library, "NDIlib_initialize");
        private readonly delegate* unmanaged[Cdecl]<NdiSendCreate*, IntPtr> create = (delegate* unmanaged[Cdecl]<NdiSendCreate*, IntPtr>)Export(library, "NDIlib_send_create");
        private readonly delegate* unmanaged[Cdecl]<IntPtr, void> destroy = (delegate* unmanaged[Cdecl]<IntPtr, void>)Export(library, "NDIlib_send_destroy");
        private readonly delegate* unmanaged[Cdecl]<IntPtr, NdiVideoFrame*, void> video = (delegate* unmanaged[Cdecl]<IntPtr, NdiVideoFrame*, void>)Export(library, "NDIlib_send_send_video_v2");

        public bool Initialize() => initialize() != 0;

        /// <summary>A sender seen on the network as <paramref name="name"/>, or zero where the runtime refused one.</summary>
        public IntPtr CreateSender(string name)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(name);

            try
            {
                var settings = new NdiSendCreate { Name = utf8 };

                return create(&settings);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        public void DestroySender(IntPtr sender) => destroy(sender);

        /// <summary>Sends a frame and returns once NDI is done with its pixels.</summary>
        public void SendVideo(IntPtr sender, in NdiVideoFrame frame)
        {
            var copy = frame;
            video(sender, &copy);
        }

        private static IntPtr Export(IntPtr library, string name) =>
            NativeLibrary.TryGetExport(library, name, out var address) ? address : throw new EntryPointNotFoundException(name);
    }
}
