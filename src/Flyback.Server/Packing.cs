using System.IO.Compression;

namespace Flyback.Server;

/// <summary>Which stored files are kept brotli-packed, and the packing of them.</summary>
/// <remarks>A <c>.fbk</c> is JSON and packs to a seventh; a bundle is already a zip and stays as it came.</remarks>
internal static class Packing
{
    public static bool Packs(string fileName) =>
        Path.GetExtension(fileName).Equals(".fbk", StringComparison.OrdinalIgnoreCase);

    public static byte[] Pack(byte[] file)
    {
        using var packed = new MemoryStream();

        using (var brotli = new BrotliStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(file);

        return packed.ToArray();
    }

    public static byte[] Unpack(byte[] packed)
    {
        using var unpacked = new MemoryStream();
        using var brotli = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress);

        brotli.CopyTo(unpacked);

        return unpacked.ToArray();
    }
}
