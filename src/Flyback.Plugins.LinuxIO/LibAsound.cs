using System.Runtime.InteropServices;

namespace Flyback.Plugins.LinuxIO;

/// <summary>
/// The slice of libasound this plugin needs. Hand-written for the same reason
/// the macOS one is: eight entry points do not justify a binding package, and
/// <see cref="Flyback.Plugins"/> having no dependencies is worth keeping true
/// one level down as well.
/// </summary>
internal static partial class LibAsound
{
    /// <summary>
    /// The SONAME, not <c>libasound</c>. The unversioned symlink only exists
    /// where the development package is installed, which on a machine that is
    /// merely playing sound it is not. Internal rather than private because the
    /// sequencer half of this plugin imports from the same library, and one
    /// SONAME written twice is one that can come to disagree with itself.
    /// </summary>
    internal const string Library = "libasound.so.2";

    /// <summary>
    /// <c>default</c> is the device that follows the system: on a desktop it is
    /// PipeWire or PulseAudio's ALSA plugin, and on a bare machine the card
    /// itself. Naming <c>hw:0</c> instead would take the card exclusively and
    /// silence everything else on the machine.
    /// </summary>
    public const string DefaultDevice = "default";

    public const int PlaybackStream = 0;    // SND_PCM_STREAM_PLAYBACK
    public const int Blocking = 0;          // the absence of SND_PCM_NONBLOCK
    public const int InterleavedAccess = 3; // SND_PCM_ACCESS_RW_INTERLEAVED

    /// <summary>Resample in software if the card cannot do the rate we asked for.</summary>
    public const int SoftwareResample = 1;

    /// <summary>
    /// <c>SND_PCM_FORMAT_FLOAT_LE</c> or <c>_BE</c>. The C header picks between
    /// them with the preprocessor, which leaves nothing for a binding to import.
    /// </summary>
    public static int NativeFloatFormat => BitConverter.IsLittleEndian ? 14 : 15;

    [LibraryImport(Library, EntryPoint = "snd_pcm_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(out IntPtr pcm, string name, int stream, int mode);

    /// <summary>
    /// The whole hardware and software parameter dance in one call. Latency is
    /// in microseconds, and libasound picks a period of about a quarter of it.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_pcm_set_params")]
    public static partial int SetParams(
        IntPtr pcm, int format, int access, uint channels, uint rate, int softResample, uint latencyMicroseconds);

    /// <summary>
    /// Blocks until the frames are accepted. Returns frames written, which may
    /// be fewer than asked for, or a negative error code.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_pcm_writei")]
    public static unsafe partial nint WriteInterleaved(IntPtr pcm, float* buffer, nuint frames);

    /// <summary>
    /// Puts the stream back after an underrun or a suspend, which are the two
    /// failures that are not the caller's fault and not fatal.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_pcm_recover")]
    public static partial int Recover(IntPtr pcm, int error, int silent);

    /// <summary>Stops now and discards what is queued, as against draining it.</summary>
    [LibraryImport(Library, EntryPoint = "snd_pcm_drop")]
    public static partial int Drop(IntPtr pcm);

    [LibraryImport(Library, EntryPoint = "snd_pcm_close")]
    public static partial int Close(IntPtr pcm);

    /// <summary>
    /// Every device of one interface ALSA's configuration names, on every card when
    /// <paramref name="card"/> is -1. The array is ALSA's and ends at a null; give it
    /// back with <see cref="FreeHints"/>.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "snd_device_name_hint", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int Hints(int card, string iface, out IntPtr* hints);

    /// <summary>One field of a hint — <c>NAME</c>, <c>DESC</c> or <c>IOID</c> — as a string the caller frees, or null.</summary>
    [LibraryImport(Library, EntryPoint = "snd_device_name_get_hint", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr Hint(IntPtr hint, string field);

    [LibraryImport(Library, EntryPoint = "snd_device_name_free_hint")]
    private static unsafe partial int FreeHints(IntPtr* hints);

    /// <summary>
    /// Every PCM device that can play, as the name <c>snd_pcm_open</c> takes and the
    /// description ALSA gives it — the same list <c>aplay -L</c> prints.
    /// </summary>
    /// <remarks>
    /// Devices that capture only are left out, and so are <c>null</c>, which plays
    /// nothing, <c>default</c>, which the caller offers under its own name, and raw
    /// <c>hw:</c> routes, which the float this plugin writes would not open.
    /// </remarks>
    public static unsafe IReadOnlyList<(string Name, string Description)> PlaybackDevices()
    {
        if (Hints(-1, "pcm", out var hints) < 0 || hints == null) return [];

        var found = new List<(string, string)>();

        try
        {
            for (var hint = hints; *hint != IntPtr.Zero; hint++)
            {
                var name = Take(Hint(*hint, "NAME"));

                // Null is both directions, which is most of them.
                if (name is null or "null" or DefaultDevice || Take(Hint(*hint, "IOID")) == "Input") continue;

                // A bare hw: route takes the card exclusively and plays only the
                // formats the chip does, float rarely among them. The plughw: route
                // beside it reaches the same card and converts.
                if (name.StartsWith("hw:", StringComparison.Ordinal)) continue;

                // Two lines — the card, then what this route to it is — read as one.
                var description = Take(Hint(*hint, "DESC"))?.Replace('\n', ' ').Trim();

                found.Add((name, string.IsNullOrEmpty(description) ? name : $"{description} ({name})"));
            }
        }
        finally
        {
            _ = FreeHints(hints);
        }

        return found;
    }

    /// <summary>A string ALSA allocated, read and then freed — its allocator is the C one.</summary>
    private static unsafe string? Take(IntPtr text)
    {
        if (text == IntPtr.Zero) return null;

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            NativeMemory.Free((void*)text);
        }
    }

    [LibraryImport(Library, EntryPoint = "snd_strerror")]
    private static partial IntPtr ErrorString(int error);

    public static string Describe(int error) =>
        Marshal.PtrToStringUTF8(ErrorString(error)) ?? $"error {error}";

    /// <summary>
    /// Whether libasound is on this machine at all. A question, not an open
    /// device — but it has to be asked, because a container or a server install
    /// often has no sound library, and without this the answer would arrive as
    /// a <see cref="DllNotFoundException"/> from the first attempt to play, or
    /// to list what is plugged in. Both halves of this plugin ask it: the
    /// sequencer is the same library, so it is the same question.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            if (!NativeLibrary.TryLoad(Library, out var handle)) return false;

            // Balances this load only; the one the entry points above use is
            // the runtime's own and is unaffected.
            NativeLibrary.Free(handle);
            return true;
        }
    }
}
