using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Flyback.Core.Compile;

namespace Flyback.Core.Render;

/// <summary>Why a picture could not be read, for the complaint that says so.</summary>
public enum PngFault
{
    None,
    Missing,
    NotPng,
    Unsupported,
    Corrupt,
    Empty,
}

/// <summary>
/// Minimal PNG decoder, and <see cref="PngWriter"/> read backwards.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand for the reason the writer was: the core carries no imaging
/// dependency (ADR-0019), and the one thing that must work headlessly is a
/// picture going in and out of a file. That the two are a pair is worth more
/// than it sounds — what this program exports is a PNG, so what it can read is
/// what it can write, and a frame can be taken back into a patch exactly as it
/// left.
/// </para>
/// <para>
/// The compression is not hand-written and did not need to be:
/// <see cref="DeflateStream"/> is in the framework, which is where the writer
/// already gets its deflate. So this is chunk-walking, un-filtering and
/// unpacking, and the hard half of the format was never ours to do.
/// </para>
/// <para>
/// What it reads is every colour type at 8 and 16 bits — grey, truecolour,
/// palette, and either of the first two with alpha — which is everything a
/// non-interlaced PNG can be. Interlaced ones are refused by name rather than
/// read wrongly: Adam7 is seven passes with their own filtering, it is rare
/// enough that nothing here has ever produced one, and a file that says it is
/// interlaced and comes back scrambled is worse than one that says it cannot be
/// read. Bit depths under eight are refused for the same reason and are rarer
/// still.
/// </para>
/// <para>
/// Alpha is multiplied in rather than kept. Three channels is what an op carries
/// and what a colour is here, and a transparent corner reading as a black one is
/// the same answer <see cref="LoadedImage.At"/> gives for a place outside the
/// picture — so a patch sees one rule rather than two.
/// </para>
/// </remarks>
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The most pixels a picture may hold, which is about the frame a very large
    /// display would want and forty times what a preview is. A cap because a
    /// header is four bytes of width and four of height, and a file claiming a
    /// billion of each would otherwise be asked for as an allocation before
    /// anything had a chance to disbelieve it.
    /// </summary>
    private const long MostPixels = 64L * 1024 * 1024;

    public static LoadedImage? Read(string path, out PngFault fault)
    {
        try
        {
            if (!File.Exists(path))
            {
                fault = PngFault.Missing;
                return null;
            }

            using var file = File.OpenRead(path);
            return Read(file, out fault);
        }
        catch (IOException)
        {
            fault = PngFault.Missing;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            fault = PngFault.Missing;
            return null;
        }
    }

    public static LoadedImage? Read(Stream input, out PngFault fault)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            return Decode(input, out fault);
        }
        catch (InvalidDataException)
        {
            // What DeflateStream throws at a payload that is not deflate, which
            // is a corrupt file rather than an unreadable one.
            fault = PngFault.Corrupt;
            return null;
        }
        catch (EndOfStreamException)
        {
            fault = PngFault.Corrupt;
            return null;
        }
    }

    private static LoadedImage? Decode(Stream input, out PngFault fault)
    {
        fault = PngFault.None;

        Span<byte> signature = stackalloc byte[8];
        if (!Fill(input, signature) || !signature.SequenceEqual(Signature))
        {
            fault = PngFault.NotPng;
            return null;
        }

        var header = default(Header);
        var palette = Array.Empty<byte>();
        var alphas = Array.Empty<byte>();
        var payload = new MemoryStream();
        var seen = false;

        while (true)
        {
            if (!Chunk(input, out var name, out var data)) break;

            switch (name)
            {
                case "IHDR":
                    if (data.Length < 13) { fault = PngFault.Corrupt; return null; }

                    header = new Header(
                        BinaryPrimitives.ReadInt32BigEndian(data),
                        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)),
                        data[8],
                        data[9],
                        data[12]);

                    seen = true;
                    break;

                case "PLTE":
                    palette = data;
                    break;

                // Palette transparency, which is the only alpha a paletted file
                // can have. Shorter than the palette is legal and means the rest
                // is opaque.
                case "tRNS":
                    alphas = data;
                    break;

                case "IDAT":
                    payload.Write(data);
                    break;

                case "IEND":
                    return Build(header, seen, palette, alphas, payload, out fault);
            }
        }

        return Build(header, seen, palette, alphas, payload, out fault);
    }

    private static LoadedImage? Build(
        Header header, bool seen, byte[] palette, byte[] alphas, MemoryStream payload, out PngFault fault)
    {
        fault = PngFault.None;

        if (!seen)
        {
            fault = PngFault.NotPng;
            return null;
        }

        if (header.Width <= 0 || header.Height <= 0
            || (long)header.Width * header.Height > MostPixels)
        {
            fault = PngFault.Empty;
            return null;
        }

        if (header.Interlace != 0 || header.Depth is not (8 or 16) || !Channels(header, out var channels))
        {
            fault = PngFault.Unsupported;
            return null;
        }

        if (payload.Length == 0)
        {
            fault = PngFault.Empty;
            return null;
        }

        var bytesPerPixel = channels * (header.Depth / 8);
        var rowLength = header.Width * bytesPerPixel;

        var raw = Inflate(payload, (long)(rowLength + 1) * header.Height);

        if (raw.Length < (rowLength + 1) * (long)header.Height)
        {
            fault = PngFault.Corrupt;
            return null;
        }

        Unfilter(raw, header.Height, rowLength, bytesPerPixel);

        return new LoadedImage(
            ToPixels(raw, header, channels, bytesPerPixel, rowLength, palette, alphas),
            header.Width,
            header.Height);
    }

    /// <summary>
    /// The zlib stream the IDAT chunks hold, which is deflate with two bytes in
    /// front of it and a checksum behind — the same wrapper the writer puts on.
    /// </summary>
    private static byte[] Inflate(MemoryStream payload, long expected)
    {
        payload.Position = 2;

        using var inflate = new DeflateStream(payload, CompressionMode.Decompress);
        using var raw = new MemoryStream((int)Math.Min(expected, 1 << 24));

        inflate.CopyTo(raw);

        return raw.GetBuffer().AsSpan(0, (int)raw.Length).ToArray();
    }

    /// <summary>
    /// Undoes the per-row prediction a PNG stores instead of the pixels
    /// themselves. Five filters, each a different guess at what a byte will be
    /// from the ones left of and above it, and each undone by adding that guess
    /// back — in place, because every row's guess is made of the row already
    /// undone above it.
    /// </summary>
    private static void Unfilter(byte[] raw, int height, int rowLength, int bytesPerPixel)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * (rowLength + 1);
            var filter = raw[row];
            var above = row - rowLength;

            for (var i = 0; i < rowLength; i++)
            {
                var at = row + 1 + i;

                var left = i >= bytesPerPixel ? raw[at - bytesPerPixel] : 0;
                var up = y > 0 ? raw[above + i] : 0;
                var corner = y > 0 && i >= bytesPerPixel ? raw[above + i - bytesPerPixel] : 0;

                raw[at] = (byte)(raw[at] + filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, corner),
                    _ => 0,
                });
            }
        }
    }

    /// <summary>
    /// Whichever of the three neighbours the gradient through them lands nearest
    /// to, which is the filter that pays for itself on photographs.
    /// </summary>
    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;

        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toCorner = Math.Abs(estimate - corner);

        if (toLeft <= toUp && toLeft <= toCorner) return left;

        return toUp <= toCorner ? up : corner;
    }

    private static float[] ToPixels(
        byte[] raw,
        Header header,
        int channels,
        int bytesPerPixel,
        int rowLength,
        byte[] palette,
        byte[] alphas)
    {
        var pixels = new float[(long)header.Width * header.Height * 3];
        var wide = header.Depth == 16;

        for (var y = 0; y < header.Height; y++)
        {
            var row = y * (rowLength + 1) + 1;

            for (var x = 0; x < header.Width; x++)
            {
                var at = row + x * bytesPerPixel;
                var to = (y * header.Width + x) * 3;

                float red, green, blue, alpha = 1f;

                if (header.Colour == 3)
                {
                    // A palette index is a byte whatever the depth says, and a
                    // file naming a colour the palette does not hold is a file
                    // that will read as black rather than as a fault.
                    var index = raw[at];
                    var entry = index * 3;

                    red = entry + 2 < palette.Length ? palette[entry] / 255f : 0f;
                    green = entry + 2 < palette.Length ? palette[entry + 1] / 255f : 0f;
                    blue = entry + 2 < palette.Length ? palette[entry + 2] / 255f : 0f;

                    if (index < alphas.Length) alpha = alphas[index] / 255f;
                }
                else
                {
                    var first = Channel(raw, at, wide);

                    red = first;
                    green = channels >= 3 ? Channel(raw, at + (wide ? 2 : 1), wide) : first;
                    blue = channels >= 3 ? Channel(raw, at + (wide ? 4 : 2), wide) : first;

                    if (channels is 2 or 4)
                        alpha = Channel(raw, at + (channels - 1) * (wide ? 2 : 1), wide);
                }

                pixels[to] = red * alpha;
                pixels[to + 1] = green * alpha;
                pixels[to + 2] = blue * alpha;
            }
        }

        return pixels;
    }

    private static float Channel(byte[] raw, int at, bool wide) =>
        wide ? (raw[at] << 8 | raw[at + 1]) / 65535f : raw[at] / 255f;

    /// <summary>How many numbers a pixel is stored as, and false for a colour type there is no such thing as.</summary>
    private static bool Channels(Header header, out int channels)
    {
        channels = header.Colour switch
        {
            0 => 1, // grey
            2 => 3, // truecolour
            3 => 1, // palette index
            4 => 2, // grey and alpha
            6 => 4, // truecolour and alpha
            _ => 0,
        };

        // A paletted file is a byte an index whatever it claims, and sixteen bits
        // of index is not a thing the format has.
        return channels > 0 && (header.Colour != 3 || header.Depth == 8);
    }

    /// <summary>
    /// One chunk: a length, a four-letter name, that many bytes and a checksum
    /// which is not looked at. The length is trusted only as far as the file
    /// actually goes, so a truncated one runs out rather than asking for a
    /// gigabyte.
    /// </summary>
    private static bool Chunk(Stream input, out string name, out byte[] data)
    {
        name = string.Empty;
        data = [];

        Span<byte> head = stackalloc byte[8];
        if (!Fill(input, head)) return false;

        var length = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (length > int.MaxValue - 16) return false;

        name = Encoding.ASCII.GetString(head[4..]);
        data = new byte[length];

        if (!Fill(input, data)) return false;

        Span<byte> checksum = stackalloc byte[4];
        Fill(input, checksum);

        return true;
    }

    private static bool Fill(Stream input, Span<byte> into)
    {
        var got = 0;

        while (got < into.Length)
        {
            var read = input.Read(into[got..]);
            if (read <= 0) return false;

            got += read;
        }

        return true;
    }

    private readonly record struct Header(int Width, int Height, byte Depth, byte Colour, byte Interlace);
}
