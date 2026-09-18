using System.Buffers.Binary;
using System.Text;

namespace Flyback.Core.Render;

/// <summary>
/// Minimal RIFF/WAVE encoder for interleaved float samples, written as 16-bit
/// PCM. The direct counterpart to <see cref="PngWriter"/>, and for the same
/// reason: offline export is the one thing that has to work headlessly, so it
/// should not depend on an audio library or a sound device being present.
/// </summary>
public static class WavWriter
{
    private const int BitsPerSample = 16;

    public static void Write(string path, ReadOnlySpan<float> interleaved, int sampleRate, int channels)
    {
        using var file = File.Create(path);
        Write(file, interleaved, sampleRate, channels);
    }

    /// <summary>Where the RIFF size sits, for whoever has to go back and fill it in.</summary>
    internal const int RiffSizeOffset = 4;

    /// <summary>Where the data size sits, likewise.</summary>
    internal const int DataSizeOffset = 40;

    /// <summary>How much of the file is header, before any sample.</summary>
    internal const int HeaderBytes = 44;

    public static void Write(Stream output, ReadOnlySpan<float> interleaved, int sampleRate, int channels)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));

        WriteHeader(output, interleaved.Length * sizeof(short), sampleRate, channels);

        Span<byte> sample = stackalloc byte[sizeof(short)];
        foreach (var value in interleaved)
        {
            BinaryPrimitives.WriteInt16LittleEndian(sample, ToPcm16(value));
            output.Write(sample);
        }
    }

    /// <summary>
    /// The 44 bytes every WAV starts with. Shared with
    /// <see cref="WavStreamWriter"/>, which writes it claiming nothing and comes
    /// back for the two sizes once the take has ended — so a recorded file and an
    /// exported one differ in nothing but how they learned their length.
    /// </summary>
    /// <param name="output"></param>
    /// <param name="dataBytes">Length of the sample data that will follow, in bytes.</param>
    /// <param name="sampleRate"></param>
    /// <param name="channels"></param>
    internal static void WriteHeader(Stream output, int dataBytes, int sampleRate, int channels)
    {
        var byteRate = sampleRate * channels * (BitsPerSample / 8);
        var blockAlign = channels * (BitsPerSample / 8);

        Span<byte> header = stackalloc byte[HeaderBytes];

        Ascii(header[..4], "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(RiffSizeOffset, 4), 36 + dataBytes);
        Ascii(header.Slice(8, 4), "WAVE");

        Ascii(header.Slice(12, 4), "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16, 4), 16);   // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(20, 2), 1);    // format: PCM
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(22, 2), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(34, 2), BitsPerSample);

        Ascii(header.Slice(36, 4), "data");
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(DataSizeOffset, 4), dataBytes);

        output.Write(header);
    }

    /// <summary>
    /// Scales by 32767 rather than 32768 so that +1.0 and -1.0 are both
    /// representable without one of them wrapping.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="AviWriter"/>, whose audio stream is the same PCM
    /// in a different container. Two conversions would be two chances to differ.
    /// </remarks>
    internal static short ToPcm16(float value)
    {
        if (!float.IsFinite(value)) return 0;

        return (short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue);
    }

    private static void Ascii(Span<byte> destination, string text) =>
        Encoding.ASCII.GetBytes(text, destination);
}
