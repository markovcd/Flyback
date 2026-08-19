using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Flyback.Core.Render;

/// <summary>
/// Minimal PNG encoder for BGRA8888 frames. Writing this by hand keeps the core
/// free of any imaging dependency, which matters because frame export is the one
/// thing that has to work headlessly.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static void WriteBgra(string path, ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        using var file = File.Create(path);
        WriteBgra(file, bgra, width, height, stride);
    }

    public static void WriteBgra(Stream output, ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // color type: truecolor RGB
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace
        WriteChunk(output, "IHDR", header);

        WriteChunk(output, "IDAT", Compress(ToFilteredRgb(bgra, width, height, stride)));
        WriteChunk(output, "IEND", []);
    }

    /// <summary>Drops alpha, reorders to RGB and prefixes each row with filter type 0.</summary>
    private static byte[] ToFilteredRgb(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var rowLength = width * 3 + 1;
        var raw = new byte[rowLength * height];

        for (var y = 0; y < height; y++)
        {
            var source = bgra.Slice(y * stride, width * 4);
            var target = y * rowLength + 1;

            for (var x = 0; x < width; x++)
            {
                raw[target + x * 3 + 0] = source[x * 4 + 2];
                raw[target + x * 3 + 1] = source[x * 4 + 1];
                raw[target + x * 3 + 2] = source[x * 4 + 0];
            }
        }

        return raw;
    }

    /// <summary>Raw deflate wrapped in the zlib header and Adler-32 trailer PNG expects.</summary>
    private static byte[] Compress(byte[] raw)
    {
        using var buffer = new MemoryStream();
        buffer.WriteByte(0x78); // zlib: deflate, 32K window
        buffer.WriteByte(0x9C); // default compression, no preset dictionary

        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw);

        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
        buffer.Write(adler);

        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        output.Write(checksum);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;

        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var c = 0xFFFFFFFFu;

        foreach (var value in first)
            c = CrcTable[(c ^ value) & 0xFF] ^ (c >> 8);

        foreach (var value in second)
            c = CrcTable[(c ^ value) & 0xFF] ^ (c >> 8);

        return c ^ 0xFFFFFFFFu;
    }
}
