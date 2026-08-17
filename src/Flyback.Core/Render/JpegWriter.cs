namespace Flyback.Core.Render;

/// <summary>
/// Baseline JPEG encoder for BGRA8888 frames: 4:2:0 chroma, the standard
/// quantisation and Huffman tables from the specification's Annex K, and no
/// restart markers.
/// </summary>
/// <remarks>
/// The third encoder written by hand here, for the reason the other two exist
/// (<see cref="PngWriter"/>, <see cref="WavWriter"/>): offline export has to
/// work headlessly, and the engine takes no dependencies. This one earns its
/// length by what it saves. A minute of 960x540 is 2.7 GB as raw pixels and
/// about 90 MB once every frame is a JPEG, and the difference between those two
/// numbers is the difference between a video export existing and not.
///
/// An instance rather than a static class, unlike its two siblings, because a
/// movie is thousands of calls rather than one: the colour planes are the
/// largest thing here and reusing them across frames costs nothing.
/// </remarks>
public sealed class JpegWriter
{
    public const int DefaultQuality = 85;

    /// <summary>One MCU is four luma blocks over one of each chroma, so 16x16 pixels.</summary>
    private const int McuSize = 16;

    private readonly byte[] lumaQuant = new byte[64];
    private readonly byte[] chromaQuant = new byte[64];

    // Full-resolution luma, half-resolution chroma. Grown to fit, never shrunk.
    private byte[] luma = [];
    private byte[] blueChroma = [];
    private byte[] redChroma = [];
    private int lumaWidth;
    private int lumaHeight;
    private int chromaWidth;
    private int chromaHeight;

    private readonly double[] samples = new double[64];
    private readonly double[] frequencies = new double[64];
    private readonly int[] coefficients = new int[64];

    /// <param name="quality">
    /// 1 to 100, scaling the Annex K tables the way every other encoder does it.
    /// Above about 95 the tables are all ones and the file grows sharply for no
    /// visible return.
    /// </param>
    public JpegWriter(int quality = DefaultQuality)
    {
        Quality = Math.Clamp(quality, 1, 100);

        Scale(StandardLumaQuant, lumaQuant, Quality);
        Scale(StandardChromaQuant, chromaQuant, Quality);
    }

    public int Quality { get; }

    /// <summary>Encodes one frame. The stream is written from wherever it is now.</summary>
    public void WriteBgra(Stream output, ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A frame needs both dimensions.");
        if (width > ushort.MaxValue || height > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "JPEG holds each dimension in sixteen bits.");
        if (bgra.Length < (long)stride * (height - 1) + width * 4)
            throw new ArgumentException("Source is smaller than the frame it describes.", nameof(bgra));

        Separate(bgra, width, height, stride);

        WriteMarker(output, 0xD8);                          // SOI
        WriteApp0(output);
        WriteQuantTables(output);
        WriteFrameHeader(output, width, height);
        WriteHuffmanTables(output);
        WriteScanHeader(output);
        WriteScan(output);
        WriteMarker(output, 0xD9);                          // EOI
    }

    // --- colour ------------------------------------------------------------------

    /// <summary>
    /// BGRA to three planes of BT.601 YCbCr, chroma boxed down to half in each
    /// direction. Done for the whole frame rather than per block: the chroma
    /// average spans a 2x2 of source pixels, and reading those from the block
    /// loop would convert every pixel twice.
    /// </summary>
    private void Separate(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        EnsurePlanes(width, height);

        for (var y = 0; y < height; y++)
        {
            var source = bgra.Slice(y * stride, width * 4);
            var target = y * width;

            for (var x = 0; x < width; x++)
            {
                double b = source[x * 4 + 0], g = source[x * 4 + 1], r = source[x * 4 + 2];
                luma[target + x] = Clamp8(0.299d * r + 0.587d * g + 0.114d * b);
            }
        }

        for (var y = 0; y < chromaHeight; y++)
        {
            // The odd row and column of an odd-sized frame have no partner, so
            // the box shrinks rather than reading past the edge.
            var y0 = y * 2;
            var y1 = Math.Min(y0 + 1, height - 1);

            for (var x = 0; x < chromaWidth; x++)
            {
                var x0 = x * 2;
                var x1 = Math.Min(x0 + 1, width - 1);

                double r = 0d, g = 0d, b = 0d;

                Accumulate(bgra, stride, x0, y0, ref r, ref g, ref b);
                Accumulate(bgra, stride, x1, y0, ref r, ref g, ref b);
                Accumulate(bgra, stride, x0, y1, ref r, ref g, ref b);
                Accumulate(bgra, stride, x1, y1, ref r, ref g, ref b);

                r *= 0.25d;
                g *= 0.25d;
                b *= 0.25d;

                var sample = y * chromaWidth + x;
                blueChroma[sample] = Clamp8(128d - 0.168736d * r - 0.331264d * g + 0.5d * b);
                redChroma[sample] = Clamp8(128d + 0.5d * r - 0.418688d * g - 0.081312d * b);
            }
        }
    }

    private static void Accumulate(ReadOnlySpan<byte> bgra, int stride, int x, int y, ref double r, ref double g, ref double b)
    {
        var pixel = y * stride + x * 4;
        b += bgra[pixel + 0];
        g += bgra[pixel + 1];
        r += bgra[pixel + 2];
    }

    private void EnsurePlanes(int width, int height)
    {
        lumaWidth = width;
        lumaHeight = height;
        chromaWidth = (width + 1) / 2;
        chromaHeight = (height + 1) / 2;

        if (luma.Length < width * height) luma = new byte[width * height];

        var chroma = chromaWidth * chromaHeight;
        if (blueChroma.Length < chroma)
        {
            blueChroma = new byte[chroma];
            redChroma = new byte[chroma];
        }
    }

    private static byte Clamp8(double v) => (byte)Math.Clamp(Math.Round(v), 0d, 255d);

    // --- entropy-coded data ------------------------------------------------------

    /// <summary>
    /// Walks the frame in 16x16 macroblocks, each of them four luma blocks and
    /// one block of each chroma. DC is coded as a difference from the previous
    /// block of the same component, so the three predictors run the length of
    /// the scan.
    /// </summary>
    private void WriteScan(Stream output)
    {
        var bits = new BitWriter(output);

        int lumaDc = 0, blueDc = 0, redDc = 0;

        var across = (lumaWidth + McuSize - 1) / McuSize;
        var down = (lumaHeight + McuSize - 1) / McuSize;

        for (var mcuY = 0; mcuY < down; mcuY++)
        for (var mcuX = 0; mcuX < across; mcuX++)
        {
            for (var block = 0; block < 4; block++)
            {
                Gather(luma, lumaWidth, lumaHeight, mcuX * McuSize + block % 2 * 8, mcuY * McuSize + block / 2 * 8);
                lumaDc = Encode(bits, lumaQuant, LumaDcCodes, LumaAcCodes, lumaDc);
            }

            Gather(blueChroma, chromaWidth, chromaHeight, mcuX * 8, mcuY * 8);
            blueDc = Encode(bits, chromaQuant, ChromaDcCodes, ChromaAcCodes, blueDc);

            Gather(redChroma, chromaWidth, chromaHeight, mcuX * 8, mcuY * 8);
            redDc = Encode(bits, chromaQuant, ChromaDcCodes, ChromaAcCodes, redDc);
        }

        bits.Flush();
    }

    /// <summary>
    /// Reads an 8x8 block out of a plane, level-shifted by -128. A frame is
    /// almost never a whole number of macroblocks, so the edges are replicated
    /// rather than padded with a colour — a hard edge against grey is a step the
    /// transform then has to spend its coefficients describing.
    /// </summary>
    private void Gather(byte[] plane, int width, int height, int left, int top)
    {
        for (var y = 0; y < 8; y++)
        {
            var row = Math.Min(top + y, height - 1) * width;

            for (var x = 0; x < 8; x++)
                samples[y * 8 + x] = plane[row + Math.Min(left + x, width - 1)] - 128d;
        }
    }

    /// <summary>Transforms, quantises and codes the block now in <see cref="samples"/>.</summary>
    /// <returns>The DC coefficient, which the next block of this component predicts from.</returns>
    private int Encode(BitWriter bits, byte[] quant, HuffmanCode[] dc, HuffmanCode[] ac, int previousDc)
    {
        Transform();

        for (var i = 0; i < 64; i++)
        {
            var zigzag = Zigzag[i];

            // Clamped to what the Annex K tables can spell. The transform is
            // orthonormal, so a coefficient reaches 1024 — eleven bits — exactly
            // when the block is the basis function itself at full contrast, and
            // the standard AC table stops at ten. Nothing a camera produces gets
            // near it and a synthesised checkerboard at quality 100 does, which
            // is precisely the sort of picture this program makes.
            coefficients[i] = Math.Clamp((int)Math.Round(frequencies[zigzag] / quant[zigzag]), -1023, 1023);
        }

        var difference = coefficients[0] - previousDc;
        var size = BitLength(difference);
        bits.Write(dc[size]);
        bits.WriteValue(difference, size);

        // Runs of zeroes are what makes this small: after quantisation most of
        // the high frequencies are gone, and everything past the last survivor
        // is said once as end-of-block.
        var run = 0;
        for (var i = 1; i < 64; i++)
        {
            if (coefficients[i] == 0)
            {
                run++;
                continue;
            }

            // A run only reaches fifteen in one symbol, so longer ones are
            // spelled out sixteen zeroes at a time.
            while (run > 15)
            {
                bits.Write(ac[0xF0]);
                run -= 16;
            }

            var magnitude = BitLength(coefficients[i]);
            bits.Write(ac[(run << 4) | magnitude]);
            bits.WriteValue(coefficients[i], magnitude);
            run = 0;
        }

        if (run > 0) bits.Write(ac[0x00]);

        return coefficients[0];
    }

    /// <summary>
    /// The forward DCT, separably: eight-point transform along each row, then
    /// along each column of the result. Written as two matrix passes rather than
    /// as one of the fast factorisations because the cost that matters in an
    /// export is evaluating the patch, not this.
    /// </summary>
    private void Transform()
    {
        Span<double> intermediate = stackalloc double[64];

        for (var y = 0; y < 8; y++)
        for (var u = 0; u < 8; u++)
        {
            var sum = 0d;
            for (var x = 0; x < 8; x++) sum += Basis[u * 8 + x] * samples[y * 8 + x];
            intermediate[y * 8 + u] = sum;
        }

        for (var u = 0; u < 8; u++)
        for (var v = 0; v < 8; v++)
        {
            var sum = 0d;
            for (var y = 0; y < 8; y++) sum += Basis[v * 8 + y] * intermediate[y * 8 + u];
            frequencies[v * 8 + u] = sum;
        }
    }

    /// <summary>How many bits the magnitude of a coefficient needs. Zero needs none.</summary>
    private static int BitLength(int value)
    {
        var magnitude = Math.Abs(value);
        var bits = 0;
        while (magnitude > 0)
        {
            magnitude >>= 1;
            bits++;
        }

        return bits;
    }

    // --- markers -----------------------------------------------------------------

    private static void WriteMarker(Stream output, byte code)
    {
        output.WriteByte(0xFF);
        output.WriteByte(code);
    }

    /// <summary>Segment marker followed by its length, which counts the two length bytes.</summary>
    private static void WriteSegment(Stream output, byte code, int payload)
    {
        WriteMarker(output, code);
        output.WriteByte((byte)((payload + 2) >> 8));
        output.WriteByte((byte)(payload + 2));
    }

    private static void WriteApp0(Stream output)
    {
        WriteSegment(output, 0xE0, 14);
        output.Write("JFIF\0"u8);
        output.WriteByte(1);        // version 1.1
        output.WriteByte(1);
        output.WriteByte(0);        // no density units
        output.WriteByte(0);
        output.WriteByte(1);        // x density
        output.WriteByte(0);
        output.WriteByte(1);        // y density
        output.WriteByte(0);
        output.WriteByte(0);        // no thumbnail
    }

    private void WriteQuantTables(Stream output)
    {
        WriteSegment(output, 0xDB, 2 * 65);

        WriteQuantTable(output, 0, lumaQuant);
        WriteQuantTable(output, 1, chromaQuant);
    }

    private static void WriteQuantTable(Stream output, byte id, byte[] table)
    {
        output.WriteByte(id);       // 8-bit precision, given identifier

        for (var i = 0; i < 64; i++) output.WriteByte(table[Zigzag[i]]);
    }

    /// <summary>SOF0: baseline, three components, luma at 2x2 against chroma at 1x1.</summary>
    private static void WriteFrameHeader(Stream output, int width, int height)
    {
        WriteSegment(output, 0xC0, 15);

        output.WriteByte(8);        // bits per sample
        output.WriteByte((byte)(height >> 8));
        output.WriteByte((byte)height);
        output.WriteByte((byte)(width >> 8));
        output.WriteByte((byte)width);
        output.WriteByte(3);        // components

        Component(1, 0x22, 0);      // Y, sampled twice in each direction
        Component(2, 0x11, 1);      // Cb
        Component(3, 0x11, 1);      // Cr

        void Component(byte id, byte sampling, byte quantTable)
        {
            output.WriteByte(id);
            output.WriteByte(sampling);
            output.WriteByte(quantTable);
        }
    }

    private static void WriteHuffmanTables(Stream output)
    {
        Table(0x00, LumaDcBits, LumaDcValues);
        Table(0x10, LumaAcBits, LumaAcValues);
        Table(0x01, ChromaDcBits, ChromaDcValues);
        Table(0x11, ChromaAcBits, ChromaAcValues);

        void Table(byte id, byte[] counts, byte[] values)
        {
            WriteSegment(output, 0xC4, 1 + 16 + values.Length);
            output.WriteByte(id);   // high nibble: 0 DC, 1 AC. Low nibble: table id.
            output.Write(counts);
            output.Write(values);
        }
    }

    /// <summary>SOS: all three components in one pass, which is what baseline means.</summary>
    private static void WriteScanHeader(Stream output)
    {
        WriteSegment(output, 0xDA, 10);

        output.WriteByte(3);        // components in this scan
        output.WriteByte(1); output.WriteByte(0x00);    // Y  uses DC 0, AC 0
        output.WriteByte(2); output.WriteByte(0x11);    // Cb uses DC 1, AC 1
        output.WriteByte(3); output.WriteByte(0x11);    // Cr uses DC 1, AC 1

        output.WriteByte(0);        // first coefficient
        output.WriteByte(63);       // last coefficient
        output.WriteByte(0);        // no successive approximation
    }

    // --- bit packing -------------------------------------------------------------

    private readonly record struct HuffmanCode(ushort Code, byte Length);

    /// <summary>
    /// Packs codes most-significant bit first and stuffs a zero after any 0xFF
    /// it emits, so that nothing in the entropy-coded data can be mistaken for a
    /// marker.
    /// </summary>
    private sealed class BitWriter(Stream output)
    {
        private uint pending;
        private int held;

        public void Write(in HuffmanCode code) => Write(code.Code, code.Length);

        public void Write(uint code, int length)
        {
            pending = (pending << length) | (code & ((1u << length) - 1));
            held += length;

            while (held >= 8)
            {
                held -= 8;
                var value = (byte)(pending >> held);
                output.WriteByte(value);
                if (value == 0xFF) output.WriteByte(0x00);
            }
        }

        /// <summary>
        /// The bits of a coefficient's magnitude. A negative value is stored as
        /// one less than itself in <paramref name="length"/> bits, which is what
        /// makes the top bit the sign without spending a bit on it.
        /// </summary>
        public void WriteValue(int value, int length)
        {
            if (length == 0) return;

            Write((uint)(value < 0 ? value - 1 : value), length);
        }

        /// <summary>Pads the last byte with ones, which is never the start of a marker.</summary>
        public void Flush()
        {
            if (held > 0) Write((1u << (8 - held)) - 1, 8 - held);
        }
    }

    // --- tables ------------------------------------------------------------------

    /// <summary>
    /// M[u][x] for the eight-point transform, including the 1/2 that makes two
    /// passes of it come out at the 1/4 the two-dimensional definition wants.
    /// </summary>
    private static readonly double[] Basis = BuildBasis();

    private static double[] BuildBasis()
    {
        var basis = new double[64];

        for (var u = 0; u < 8; u++)
        {
            var normal = u == 0 ? 1d / Math.Sqrt(2d) : 1d;

            for (var x = 0; x < 8; x++)
                basis[u * 8 + x] = 0.5d * normal * Math.Cos((2 * x + 1) * u * Math.PI / 16d);
        }

        return basis;
    }

    /// <summary>Natural-order position of each coefficient in zigzag sequence.</summary>
    private static ReadOnlySpan<byte> Zigzag =>
    [
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static ReadOnlySpan<byte> StandardLumaQuant =>
    [
        16, 11, 10, 16,  24,  40,  51,  61,
        12, 12, 14, 19,  26,  58,  60,  55,
        14, 13, 16, 24,  40,  57,  69,  56,
        14, 17, 22, 29,  51,  87,  80,  62,
        18, 22, 37, 56,  68, 109, 103,  77,
        24, 35, 55, 64,  81, 104, 113,  92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103,  99,
    ];

    private static ReadOnlySpan<byte> StandardChromaQuant =>
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    /// <summary>
    /// The published tables are for one particular quality, and every encoder
    /// reaches other qualities the same way: divide them below fifty, scale them
    /// linearly above it. A step of one is never allowed to become zero.
    /// </summary>
    private static void Scale(ReadOnlySpan<byte> standard, byte[] target, int quality)
    {
        var factor = quality < 50 ? 5000 / quality : 200 - quality * 2;

        for (var i = 0; i < 64; i++)
            target[i] = (byte)Math.Clamp((standard[i] * factor + 50) / 100, 1, 255);
    }

    private static readonly byte[] LumaDcBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] LumaDcValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static readonly byte[] ChromaDcBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    private static readonly byte[] ChromaDcValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static readonly byte[] LumaAcBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    private static readonly byte[] LumaAcValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
        0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
        0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
        0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
        0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
        0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
        0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
        0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
        0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    private static readonly byte[] ChromaAcBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];

    private static readonly byte[] ChromaAcValues =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
        0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
        0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34,
        0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38,
        0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96,
        0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4,
        0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2,
        0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9,
        0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    private static readonly HuffmanCode[] LumaDcCodes = BuildCodes(LumaDcBits, LumaDcValues);
    private static readonly HuffmanCode[] LumaAcCodes = BuildCodes(LumaAcBits, LumaAcValues);
    private static readonly HuffmanCode[] ChromaDcCodes = BuildCodes(ChromaDcBits, ChromaDcValues);
    private static readonly HuffmanCode[] ChromaAcCodes = BuildCodes(ChromaAcBits, ChromaAcValues);

    /// <summary>
    /// Turns the canonical form the file carries — how many codes there are of
    /// each length, then the symbols in order — back into a code per symbol.
    /// Both the decoder and this agree on it because it is the only assignment
    /// those two lists can describe.
    /// </summary>
    private static HuffmanCode[] BuildCodes(byte[] counts, byte[] values)
    {
        var codes = new HuffmanCode[256];
        var code = 0;
        var symbol = 0;

        for (var length = 1; length <= 16; length++)
        {
            for (var i = 0; i < counts[length - 1]; i++)
                codes[values[symbol++]] = new HuffmanCode((ushort)code++, (byte)length);

            code <<= 1;
        }

        return codes;
    }
}
