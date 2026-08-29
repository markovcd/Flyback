using System.Buffers.Binary;
using System.Text;
using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>
/// Why a file could not be read as audio, or <see cref="None"/> where it could.
/// </summary>
/// <remarks>
/// A value rather than an exception, for <see cref="WavReader"/>'s callers
/// rather than for its own sake: a missing or malformed sample is something the
/// compiler says about a patch, in the same sentence it says everything else,
/// and a throw would have to be caught and turned back into one of these
/// somewhere less able to say which module it was about.
/// </remarks>
public enum WavFault
{
    None,

    /// <summary>Nothing at that path.</summary>
    Missing,

    /// <summary>Something is there and it is not a RIFF/WAVE file.</summary>
    NotWave,

    /// <summary>A WAVE this reader does not know how to read.</summary>
    Unsupported,

    /// <summary>A WAVE with no audio in it.</summary>
    Empty,
}

/// <summary>
/// Minimal RIFF/WAVE decoder, the counterpart to <see cref="WavWriter"/> and
/// written here for the same reason: reading a sample must work on a build
/// server with nothing installed, so it cannot depend on an audio library.
/// </summary>
/// <remarks>
/// <para>
/// Mixed down to mono on the way in, because the op that reads one is scalar
/// like every other signal in the machine. A stereo file becomes the average of
/// its channels, which is what a mono sum is; anything that wants the two apart
/// wants two modules, and a second socket on this one would be a stereo path
/// the rest of the instrument does not have.
/// </para>
/// <para>
/// PCM only — 8, 16, 24 and 32 bit integer, and 32 and 64 bit float, which is
/// everything a recorder or an editor writes. Compressed WAVE payloads are
/// refused by name rather than decoded: they are a codec each, and a sample
/// nobody can read is better said out loud than guessed at.
/// </para>
/// <para>
/// Chunks are walked rather than assumed, because a file from an editor
/// routinely carries LIST, cue and fact chunks between the header and the audio.
/// <see cref="WavWriter"/>'s own output is the simple case and not the only one.
/// </para>
/// </remarks>
public static class WavReader
{
    /// <summary>Uncompressed integer PCM, which is what the format tag 1 means.</summary>
    private const int FormatPcm = 1;

    /// <summary>IEEE float, tag 3 — what an editor writes when it does not want to quantise.</summary>
    private const int FormatFloat = 3;

    /// <summary>
    /// Tag 0xFFFE: the real format is in an extension field, and the first two
    /// bytes of its GUID are one of the two above.
    /// </summary>
    private const int FormatExtensible = 0xFFFE;

    /// <summary>
    /// The longest clip this will load, in samples. Ten minutes at 48 kHz, which
    /// is far past anything anybody plays from a module and well short of a file
    /// that would exhaust memory before saying anything useful.
    /// </summary>
    public const int MostSamples = 48_000 * 60 * 10;

    public static LoadedSample? Read(string path, out WavFault fault)
    {
        if (!File.Exists(path))
        {
            fault = WavFault.Missing;
            return null;
        }

        try
        {
            using var file = File.OpenRead(path);
            return Read(file, out fault);
        }
        catch (IOException)
        {
            // Locked, or on a share that went away between the check and the
            // open. Indistinguishable from missing as far as a patch is
            // concerned, and said the same way.
            fault = WavFault.Missing;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            fault = WavFault.Missing;
            return null;
        }
    }

    public static LoadedSample? Read(Stream input, out WavFault fault)
    {
        fault = WavFault.NotWave;

        var header = new byte[12];
        if (!Fill(input, header)) return null;

        if (Ascii(header, 0) != "RIFF" || Ascii(header, 8) != "WAVE") return null;

        var channels = 0;
        var rate = 0;
        var bits = 0;
        var format = 0;

        var chunk = new byte[8];

        while (Fill(input, chunk))
        {
            var name = Ascii(chunk, 0);
            var size = BinaryPrimitives.ReadInt32LittleEndian(chunk.AsSpan(4, 4));

            if (size < 0) return null;

            if (name == "fmt ")
            {
                var body = new byte[size];
                if (!Fill(input, body)) return null;
                if (size < 16) return null;

                format = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14, 2));

                // The extension carries the real tag in the first two bytes of
                // its sub-format GUID, and the rest of the GUID is the same for
                // every one of them.
                if (format == FormatExtensible && size >= 26)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(24, 2));
            }
            else if (name == "data")
            {
                if (channels <= 0 || rate <= 0)
                {
                    fault = WavFault.NotWave;
                    return null;
                }

                return Decode(input, size, format, channels, bits, rate, out fault);
            }
            else
            {
                if (!Skip(input, size)) return null;
            }

            // Chunks are padded to an even length, and the pad byte is not
            // counted in the size — a file whose text chunk happens to be odd
            // would otherwise put every chunk after it one byte out.
            if ((size & 1) == 1 && !Skip(input, 1)) return null;
        }

        // Ran out before any audio. A header with no data chunk is a WAVE in
        // shape and not a sound.
        fault = WavFault.Empty;
        return null;
    }

    private static LoadedSample? Decode(
        Stream input,
        int bytes,
        int format,
        int channels,
        int bits,
        int rate,
        out WavFault fault)
    {
        var width = bits / 8;

        if (format is not (FormatPcm or FormatFloat) || width == 0)
        {
            fault = WavFault.Unsupported;
            return null;
        }

        if (format == FormatPcm && bits is not (8 or 16 or 24 or 32))
        {
            fault = WavFault.Unsupported;
            return null;
        }

        if (format == FormatFloat && bits is not (32 or 64))
        {
            fault = WavFault.Unsupported;
            return null;
        }

        var frame = width * channels;
        var frames = Math.Min(bytes / frame, MostSamples);

        if (frames <= 0)
        {
            fault = WavFault.Empty;
            return null;
        }

        var samples = new float[frames];
        var raw = new byte[frame];

        for (var i = 0; i < frames; i++)
        {
            if (!Fill(input, raw))
            {
                // Truncated. What was read is still a sound, and a shorter one
                // is a better answer than none — the alternative is refusing a
                // file that plays perfectly in everything else.
                Array.Resize(ref samples, i);
                break;
            }

            var sum = 0d;
            for (var c = 0; c < channels; c++) sum += One(raw.AsSpan(c * width, width), format, bits);

            samples[i] = (float)(sum / channels);
        }

        if (samples.Length == 0)
        {
            fault = WavFault.Empty;
            return null;
        }

        fault = WavFault.None;
        return new LoadedSample(samples, rate);
    }

    /// <summary>One channel of one frame, as -1..1.</summary>
    private static double One(ReadOnlySpan<byte> raw, int format, int bits)
    {
        if (format == FormatFloat)
        {
            return bits == 32
                ? BinaryPrimitives.ReadSingleLittleEndian(raw)
                : BinaryPrimitives.ReadDoubleLittleEndian(raw);
        }

        return bits switch
        {
            // The one unsigned depth, and the reason it is: 8-bit WAVE predates
            // the convention every wider one follows.
            8 => (raw[0] - 128) / 128d,
            16 => BinaryPrimitives.ReadInt16LittleEndian(raw) / 32768d,
            24 => (raw[0] | (raw[1] << 8) | ((sbyte)raw[2] << 16)) / 8_388_608d,
            _ => BinaryPrimitives.ReadInt32LittleEndian(raw) / 2_147_483_648d,
        };
    }

    private static string Ascii(byte[] buffer, int at) => Encoding.ASCII.GetString(buffer, at, 4);

    /// <summary>Reads exactly as many bytes as the buffer wants, or answers no.</summary>
    private static bool Fill(Stream input, Span<byte> buffer)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            var got = input.Read(buffer[read..]);
            if (got <= 0) return false;

            read += got;
        }

        return true;
    }

    private static bool Skip(Stream input, int bytes)
    {
        if (bytes == 0) return true;

        // Seeking where the stream allows it and reading where it does not, so
        // a file with a large chunk in front of the audio costs nothing and a
        // pipe still works.
        if (input.CanSeek)
        {
            if (input.Position + bytes > input.Length) return false;

            input.Seek(bytes, SeekOrigin.Current);
            return true;
        }

        var bin = new byte[Math.Min(bytes, 4096)];

        while (bytes > 0)
        {
            var got = input.Read(bin, 0, Math.Min(bytes, bin.Length));
            if (got <= 0) return false;

            bytes -= got;
        }

        return true;
    }
}
