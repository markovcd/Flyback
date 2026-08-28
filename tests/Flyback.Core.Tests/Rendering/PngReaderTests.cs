using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Flyback.Core.Compile;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// The PNG decoder, and the one test that matters most: what this program writes
/// is what it can read.
/// </summary>
/// <remarks>
/// Most of these build a file byte by byte rather than shipping one, because a
/// checked-in PNG is a fixture nobody can read and half of what is being pinned
/// here is what happens to a file that is <em>wrong</em> — truncated, interlaced,
/// a depth the reader refuses. A builder makes those as easily as it makes the
/// good case.
/// </remarks>
public class PngReaderTests
{
    // --- the round trip --------------------------------------------------------

    /// <summary>
    /// A frame written by this program and read back by it, to the eight bits it
    /// was written with. The reader and the writer are a pair, and this is the
    /// property that makes them one: a still exported from a patch can be taken
    /// back into another.
    /// </summary>
    [Fact]
    public void What_the_writer_wrote_is_what_the_reader_reads()
    {
        const int width = 23;
        const int height = 9;

        var stride = width * 4;
        var bgra = new byte[stride * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = y * stride + x * 4;

            bgra[at + 0] = (byte)(x * 11 % 256);   // blue
            bgra[at + 1] = (byte)(y * 29 % 256);   // green
            bgra[at + 2] = (byte)((x + y) * 7 % 256);
            bgra[at + 3] = 255;
        }

        var file = new MemoryStream();
        PngWriter.WriteBgra(file, bgra, width, height, stride);
        file.Position = 0;

        var read = PngReader.Read(file, out var fault).ShouldNotBeNull();

        fault.ShouldBe(PngFault.None);
        read.Width.ShouldBe(width);
        read.Height.ShouldBe(height);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var wrote = y * stride + x * 4;
            var got = (y * width + x) * 3;

            read.Pixels[got + 0].ShouldBe(bgra[wrote + 2] / 255f, 1e-6f);
            read.Pixels[got + 1].ShouldBe(bgra[wrote + 1] / 255f, 1e-6f);
            read.Pixels[got + 2].ShouldBe(bgra[wrote + 0] / 255f, 1e-6f);
        }
    }

    // --- what it can read ------------------------------------------------------

    /// <summary>
    /// Every colour type, at both depths the format has above a byte. Each is
    /// built with one known pixel in it, so what is checked is that the channels
    /// landed where they belong rather than that something came back.
    /// </summary>
    [Theory]
    [InlineData(0, 8)]   // grey
    [InlineData(0, 16)]
    [InlineData(2, 8)]   // truecolour
    [InlineData(2, 16)]
    [InlineData(4, 8)]   // grey and alpha
    [InlineData(6, 8)]   // truecolour and alpha
    [InlineData(6, 16)]
    public void Every_colour_type_reads(int colour, int depth)
    {
        var picture = Read(Png(colour, depth)).ShouldNotBeNull();

        picture.Width.ShouldBe(2);
        picture.Height.ShouldBe(2);

        // Whatever the file said, the first pixel is a full-strength red where
        // it has colour and a mid grey where it does not.
        if (colour is 2 or 6)
        {
            picture.Pixels[0].ShouldBe(1f, 1e-4f);
            picture.Pixels[1].ShouldBe(0f, 1e-4f);
            picture.Pixels[2].ShouldBe(0f, 1e-4f);
        }
        else
        {
            picture.Pixels[0].ShouldBe(0.5f, 0.01f);
            picture.Pixels[1].ShouldBe(picture.Pixels[0]);
            picture.Pixels[2].ShouldBe(picture.Pixels[0]);
        }
    }

    [Fact]
    public void A_palette_is_looked_up_and_its_transparency_taken_as_black()
    {
        // Two entries: a green, and one the tRNS chunk says is see-through.
        var palette = new byte[] { 0, 255, 0, 200, 100, 50 };
        var alphas = new byte[] { 255, 0 };

        var picture = Read(Png(3, 8, palette, alphas, [0, 1, 1, 0])).ShouldNotBeNull();

        picture.Pixels[0].ShouldBe(0f, 1e-4f);
        picture.Pixels[1].ShouldBe(1f, 1e-4f);
        picture.Pixels[2].ShouldBe(0f, 1e-4f);

        // The transparent entry, which is the colour it names multiplied by
        // nothing rather than the colour it names.
        picture.Pixels[3].ShouldBe(0f);
        picture.Pixels[4].ShouldBe(0f);
        picture.Pixels[5].ShouldBe(0f);
    }

    /// <summary>
    /// Alpha is multiplied in rather than kept, so a half-transparent colour is
    /// a half-strength one — which is the same answer a place outside the picture
    /// gets, and is why a patch sees one rule about the edges rather than two.
    /// </summary>
    [Fact]
    public void Transparency_is_taken_as_black_behind()
    {
        var picture = Read(Png(6, 8, samples: [255, 255, 255, 128])).ShouldNotBeNull();

        picture.Pixels[0].ShouldBe(128f / 255f, 1e-3f);
        picture.Pixels[1].ShouldBe(128f / 255f, 1e-3f);
    }

    /// <summary>
    /// Every filter, because they are the one part of the format this has to
    /// undo itself and the one where a mistake in the arithmetic looks like a
    /// picture rather than like a fault — a bad Paeth is a smear that a person
    /// might take for a photograph.
    /// </summary>
    [Theory]
    [InlineData(0)] // none
    [InlineData(1)] // left
    [InlineData(2)] // up
    [InlineData(3)] // average
    [InlineData(4)] // Paeth
    public void Every_row_filter_is_undone(byte filter)
    {
        const int size = 6;

        // A gradient in both directions, so every filter has something to
        // predict and no two rows are alike.
        var wanted = new byte[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var at = (y * size + x) * 3;

            wanted[at] = (byte)(x * 40);
            wanted[at + 1] = (byte)(y * 40);
            wanted[at + 2] = (byte)((x + y) * 20);
        }

        var picture = Read(Filtered(wanted, size, filter)).ShouldNotBeNull();

        for (var i = 0; i < wanted.Length; i++)
            picture.Pixels[i].ShouldBe(wanted[i] / 255f, 1e-6f, $"channel {i} under filter {filter}");
    }

    // --- what it refuses -------------------------------------------------------

    [Fact]
    public void Something_that_is_not_a_png_is_refused_as_one()
    {
        PngReader.Read(new MemoryStream(Encoding.ASCII.GetBytes("this is not a picture")), out var fault)
            .ShouldBeNull();

        fault.ShouldBe(PngFault.NotPng);
    }

    [Fact]
    public void A_missing_file_is_missing_rather_than_an_exception()
    {
        PngReader.Read(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"), out var fault)
            .ShouldBeNull();

        fault.ShouldBe(PngFault.Missing);
    }

    /// <summary>
    /// Refused by name rather than read wrongly. An interlaced file decoded as
    /// though it were not comes back scrambled, and a picture that is nonsense is
    /// worse than one that says it cannot be read.
    /// </summary>
    [Fact]
    public void An_interlaced_file_is_refused_rather_than_scrambled()
    {
        Read(Png(2, 8, interlace: 1), out var fault).ShouldBeNull();
        fault.ShouldBe(PngFault.Unsupported);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void A_depth_under_a_byte_is_refused(int depth)
    {
        Read(Png(0, depth), out var fault).ShouldBeNull();
        fault.ShouldBe(PngFault.Unsupported);
    }

    [Fact]
    public void A_truncated_file_is_corrupt_rather_than_a_crash()
    {
        var whole = Png(2, 8);
        var cut = whole.AsSpan(0, whole.Length - 20).ToArray();

        // Either answer is honest — what it must not do is throw or hand back a
        // picture made of whatever was in memory.
        Read(cut, out var fault).ShouldBeNull();
        fault.ShouldBeOneOf(PngFault.Corrupt, PngFault.NotPng, PngFault.Empty);
    }

    [Fact]
    public void A_file_with_no_pixels_in_it_is_empty()
    {
        Read(Png(2, 8, width: 0, height: 0), out var fault).ShouldBeNull();
        fault.ShouldBe(PngFault.Empty);
    }

    // --- the library -----------------------------------------------------------

    [Fact]
    public void The_library_reads_a_file_once_and_says_why_when_it_cannot()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"flyback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "one.png");
            File.WriteAllBytes(path, Png(2, 8));

            var library = new ImageLibrary { Beside = folder };

            // Named relatively, so a patch and its pictures travel together.
            var first = library.Find("one.png").ShouldNotBeNull();
            library.Find("one.png").ShouldBeSameAs(first);
            library.Count.ShouldBe(1);

            library.Explain("nowhere.png").ShouldBe("there is no file there.");

            // A failure is remembered too, and forgetting is what gives a file
            // that has since appeared another chance.
            library.Find("nowhere.png").ShouldBeNull();
            library.Forget();
            library.Count.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // --- building files ---------------------------------------------------------

    private static LoadedImage? Read(byte[] png) => Read(png, out _);

    private static LoadedImage? Read(byte[] png, out PngFault fault) =>
        PngReader.Read(new MemoryStream(png), out fault);

    /// <summary>
    /// A two-by-two PNG of whatever colour type and depth is asked for, filtered
    /// with none so that what is being tested is the unpacking rather than the
    /// prediction.
    /// </summary>
    private static byte[] Png(
        int colour,
        int depth,
        byte[]? palette = null,
        byte[]? alphas = null,
        byte[]? samples = null,
        int interlace = 0,
        int width = 2,
        int height = 2)
    {
        var channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 1 };
        var bytes = depth == 16 ? 2 : 1;

        var raw = new List<byte>();

        for (var y = 0; y < height; y++)
        {
            raw.Add(0); // filter: none

            for (var x = 0; x < width; x++)
            for (var channel = 0; channel < channels; channel++)
            {
                // The first pixel is a red, or a mid grey where there is no
                // colour to be red in; everything else is whatever is left.
                var value = samples is { } given
                    ? given[(x + y * width) * channels % given.Length + channel % given.Length]
                    : Sample(colour, x, y, channel);

                if (bytes == 2) raw.Add(value);
                raw.Add(value);
            }
        }

        return Assemble(width, height, depth, colour, interlace, [.. raw], palette, alphas);
    }

    private static byte Sample(int colour, int x, int y, int channel) => colour switch
    {
        3 => 0,
        0 or 4 when x == 0 && y == 0 => channel == 0 ? (byte)128 : (byte)255,
        2 or 6 when x == 0 && y == 0 => channel switch { 0 => 255, 3 => 255, _ => 0 },
        _ => channel == 3 ? (byte)255 : (byte)64,
    };

    /// <summary>The same picture with every row carrying one filter, for the filter tests.</summary>
    private static byte[] Filtered(byte[] rgb, int size, byte filter)
    {
        var rowLength = size * 3;
        var raw = new byte[(rowLength + 1) * size];

        for (var y = 0; y < size; y++)
        {
            var row = y * (rowLength + 1);
            raw[row] = filter;

            for (var i = 0; i < rowLength; i++)
            {
                var value = rgb[y * rowLength + i];

                var left = i >= 3 ? rgb[y * rowLength + i - 3] : 0;
                var up = y > 0 ? rgb[(y - 1) * rowLength + i] : 0;
                var corner = y > 0 && i >= 3 ? rgb[(y - 1) * rowLength + i - 3] : 0;

                // The prediction the reader will add back, subtracted here — the
                // encoder's half of the same five sums.
                var guess = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, corner),
                    _ => 0,
                };

                raw[row + 1 + i] = (byte)(value - guess);
            }
        }

        return Assemble(size, size, 8, 2, 0, raw, null, null);
    }

    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;

        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toCorner = Math.Abs(estimate - corner);

        if (toLeft <= toUp && toLeft <= toCorner) return left;

        return toUp <= toCorner ? up : corner;
    }

    private static byte[] Assemble(
        int width, int height, int depth, int colour, int interlace,
        byte[] raw, byte[]? palette, byte[]? alphas)
    {
        var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth;
        header[9] = (byte)colour;
        header[12] = (byte)interlace;

        Chunk(file, "IHDR", header);

        if (palette is { Length: > 0 }) Chunk(file, "PLTE", palette);
        if (alphas is { Length: > 0 }) Chunk(file, "tRNS", alphas);

        Chunk(file, "IDAT", Zlib(raw));
        Chunk(file, "IEND", []);

        return file.ToArray();
    }

    private static void Chunk(Stream to, string name, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);

        to.Write(length);
        to.Write(Encoding.ASCII.GetBytes(name));
        to.Write(data);

        // The checksum, which the reader does not look at — written as nothing so
        // that a test file is honest about what it is rather than pretending.
        to.Write([0, 0, 0, 0]);
    }

    private static byte[] Zlib(byte[] raw)
    {
        var buffer = new MemoryStream();
        buffer.WriteByte(0x78);
        buffer.WriteByte(0x9C);

        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw);

        buffer.Write([0, 0, 0, 0]);

        return buffer.ToArray();
    }
}
