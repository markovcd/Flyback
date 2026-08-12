using System.Runtime.InteropServices;

namespace Flyback.Plugins.Alsa;

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
    /// merely playing sound it is not.
    /// </summary>
    private const string Library = "libasound.so.2";

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

    [LibraryImport(Library, EntryPoint = "snd_strerror")]
    private static partial IntPtr ErrorString(int error);

    public static string Describe(int error) =>
        Marshal.PtrToStringUTF8(ErrorString(error)) ?? $"error {error}";

    /// <summary>
    /// Whether libasound is on this machine at all. A question, not an open
    /// device — but it has to be asked, because a container or a server install
    /// often has no sound library, and without this the answer would arrive as
    /// a <see cref="DllNotFoundException"/> from the first attempt to play.
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
