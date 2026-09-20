using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// A QR code, encoded rather than kept as a picture, and drawn as squares.
/// </summary>
/// <remarks>
/// A picture of an address is the one thing nobody can check by reading, and a
/// stale one sends the money to whoever holds the old key. Encoding it from the
/// same string the window shows means the two cannot disagree.
/// <para>
/// Byte mode at error-correction level M, versions 1 to 3, which at this size is
/// a single block of data and one of error correction. A bech32 address is 42
/// characters, and a version 3 code holds exactly that: four bits of mode, eight
/// of length and the address fill its 44 codewords to the bit.
/// </para>
/// </remarks>
public sealed class QrCode : Control
{
    /// <summary>Data codewords at level M, for versions 1 to 3.</summary>
    private static readonly int[] DataCodewords = [16, 28, 44];

    /// <summary>Error-correction codewords at level M, for versions 1 to 3.</summary>
    private static readonly int[] EccCodewords = [10, 16, 26];

    /// <summary>Where the alignment patterns are centered, for versions 1 to 3.</summary>
    private static readonly int[][] AlignmentCenters = [[], [6, 18], [6, 22]];

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<QrCode, string>(nameof(Text), string.Empty);

    /// <summary>What the code encodes.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    static QrCode() => AffectsRender<QrCode>(TextProperty);

    /// <summary>
    /// The modules of a code for <paramref name="text"/>, indexed row then column
    /// and true where the module is dark.
    /// </summary>
    /// <exception cref="ArgumentException">The text is longer than a version 3 code holds.</exception>
    public static bool[,] Modules(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var version = Version(data.Length);
        var size = 17 + 4 * version;
        var codewords = Codewords(data, version);

        bool[,]? best = null;
        var lowest = int.MaxValue;

        // Every mask is legal, and the one scoring fewest penalty points is the
        // one a reader has the least trouble with.
        for (var mask = 0; mask < 8; mask++)
        {
            var candidate = Draw(codewords, version, size, mask);
            var penalty = Penalty(candidate, size);

            if (penalty >= lowest) continue;

            lowest = penalty;
            best = candidate;
        }

        return best!;
    }

    /// <summary>The smallest version whose level-M capacity holds <paramref name="length"/> bytes.</summary>
    private static int Version(int length)
    {
        for (var version = 1; version <= 3; version++)
            if (4 + 8 + 8 * length <= 8 * DataCodewords[version - 1])
                return version;

        throw new ArgumentException($"{length} bytes is more than a version 3 code holds", nameof(length));
    }

    /// <summary>The data codewords, with their error correction after them.</summary>
    private static byte[] Codewords(byte[] data, int version)
    {
        var capacity = DataCodewords[version - 1];
        var bits = new List<bool>();

        Add(bits, 0b0100, 4);       // byte mode
        Add(bits, data.Length, 8);  // its length, eight bits for versions 1 to 9

        foreach (var value in data)
            Add(bits, value, 8);

        // As much of the terminator as there is room for, then up to a whole codeword.
        for (var i = 0; i < 4 && bits.Count < 8 * capacity; i++)
            bits.Add(false);

        while (bits.Count % 8 != 0)
            bits.Add(false);

        var message = new byte[capacity];

        for (var i = 0; i < bits.Count; i++)
            if (bits[i])
                message[i / 8] |= (byte)(0x80 >> (i % 8));

        // Anything still spare takes these two in turn, which is what the specification pads with.
        for (var i = bits.Count / 8; i < capacity; i++)
            message[i] = (i - bits.Count / 8) % 2 == 0 ? (byte)0xEC : (byte)0x11;

        return [.. message, .. Remainder(message, EccCodewords[version - 1])];
    }

    private static void Add(List<bool> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
            bits.Add((value >> i & 1) != 0);
    }

    /// <summary>The Reed-Solomon remainder of <paramref name="message"/>, which is its error correction.</summary>
    private static byte[] Remainder(byte[] message, int count)
    {
        var divisor = Divisor(count);
        var remainder = new byte[count];

        foreach (var value in message)
        {
            var factor = (byte)(value ^ remainder[0]);

            Array.Copy(remainder, 1, remainder, 0, count - 1);
            remainder[count - 1] = 0;

            for (var i = 0; i < count; i++)
                remainder[i] ^= Multiply(divisor[i], factor);
        }

        return remainder;
    }

    /// <summary>The generator polynomial for <paramref name="count"/> error-correction codewords.</summary>
    private static byte[] Divisor(int count)
    {
        var result = new byte[count];
        result[count - 1] = 1;

        var root = (byte)1;

        for (var degree = 0; degree < count; degree++)
        {
            for (var i = 0; i < count; i++)
            {
                result[i] = Multiply(result[i], root);

                if (i + 1 < count)
                    result[i] ^= result[i + 1];
            }

            root = Multiply(root, 2);
        }

        return result;
    }

    /// <summary>Multiplication in GF(256), reduced by the field's primitive polynomial.</summary>
    private static byte Multiply(byte a, byte b)
    {
        var result = 0;

        for (var i = 7; i >= 0; i--)
        {
            result = (result << 1) ^ ((result >> 7) * 0x11D);
            result ^= (b >> i & 1) * a;
        }

        return (byte)result;
    }

    /// <summary>The whole code: its patterns, the data under one mask, and the format bits.</summary>
    private static bool[,] Draw(byte[] codewords, int version, int size, int mask)
    {
        var modules = new bool[size, size];
        var reserved = new bool[size, size];

        foreach (var (row, column) in Corners(size))
            Finder(modules, reserved, size, row, column);

        // The timing patterns, running between the separators on row and column six.
        for (var i = 8; i < size - 8; i++)
        {
            modules[6, i] = modules[i, 6] = i % 2 == 0;
            reserved[6, i] = reserved[i, 6] = true;
        }

        foreach (var row in AlignmentCenters[version - 1])
        foreach (var column in AlignmentCenters[version - 1])
            if (!reserved[row, column])
                Alignment(modules, reserved, row, column);

        Reserve(reserved, size);
        Place(modules, reserved, size, codewords, mask);
        Format(modules, size, mask);

        return modules;
    }

    /// <summary>The top-left corner of each of the three finder patterns.</summary>
    private static (int Row, int Column)[] Corners(int size) => [(0, 0), (0, size - 7), (size - 7, 0)];

    /// <summary>A finder pattern and the light separator around it.</summary>
    private static void Finder(bool[,] modules, bool[,] reserved, int size, int row, int column)
    {
        for (var y = -1; y <= 7; y++)
        for (var x = -1; x <= 7; x++)
        {
            var (r, c) = (row + y, column + x);

            if (r < 0 || r >= size || c < 0 || c >= size) continue;

            var inside = y is >= 0 and <= 6 && x is >= 0 and <= 6;

            modules[r, c] = inside && Math.Max(Math.Abs(3 - y), Math.Abs(3 - x)) != 2;
            reserved[r, c] = true;
        }
    }

    /// <summary>An alignment pattern, centered where the timing patterns' grid says.</summary>
    private static void Alignment(bool[,] modules, bool[,] reserved, int row, int column)
    {
        for (var y = -2; y <= 2; y++)
        for (var x = -2; x <= 2; x++)
        {
            modules[row + y, column + x] = Math.Max(Math.Abs(y), Math.Abs(x)) != 1;
            reserved[row + y, column + x] = true;
        }
    }

    /// <summary>Keeps the format bits' places, and the dark module, clear of data.</summary>
    private static void Reserve(bool[,] reserved, int size)
    {
        for (var i = 0; i < 9; i++)
            reserved[8, i] = reserved[i, 8] = true;

        for (var i = 0; i < 8; i++)
            reserved[8, size - 1 - i] = reserved[size - 1 - i, 8] = true;
    }

    /// <summary>Lays the codewords down the zigzag, masking each module as it lands.</summary>
    private static void Place(bool[,] modules, bool[,] reserved, int size, byte[] codewords, int mask)
    {
        var bit = 0;
        var upwards = true;

        for (var right = size - 1; right >= 1; right -= 2)
        {
            // Column six is a timing pattern, so the pairs step around it.
            if (right == 6) right = 5;

            for (var step = 0; step < size; step++)
            {
                var row = upwards ? size - 1 - step : step;

                for (var column = right; column > right - 2; column--)
                {
                    if (reserved[row, column]) continue;

                    // The last few modules of a code have no codeword to take, and stay light.
                    var dark = bit < 8 * codewords.Length && (codewords[bit / 8] >> (7 - bit % 8) & 1) != 0;

                    modules[row, column] = dark ^ Masked(row, column, mask);
                    bit++;
                }
            }

            upwards = !upwards;
        }
    }

    /// <summary>Whether the mask flips the module in this place.</summary>
    private static bool Masked(int row, int column, int mask) => mask switch
    {
        0 => (row + column) % 2 == 0,
        1 => row % 2 == 0,
        2 => column % 3 == 0,
        3 => (row + column) % 3 == 0,
        4 => (row / 2 + column / 3) % 2 == 0,
        5 => row * column % 2 + row * column % 3 == 0,
        6 => (row * column % 2 + row * column % 3) % 2 == 0,
        _ => ((row + column) % 2 + row * column % 3) % 2 == 0,
    };

    /// <summary>
    /// The format bits — level M and the mask under their BCH code — written beside
    /// the top-left finder and again split between the other two.
    /// </summary>
    private static void Format(bool[,] modules, int size, int mask)
    {
        // Level M is 0b00, so the five bits the code is taken over are the mask alone.
        var format = mask;
        var remainder = format;

        for (var i = 0; i < 10; i++)
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);

        var bits = (format << 10 | remainder) ^ 0x5412;

        for (var i = 0; i <= 5; i++)
            modules[i, 8] = Bit(bits, i);

        modules[7, 8] = Bit(bits, 6);
        modules[8, 8] = Bit(bits, 7);
        modules[8, 7] = Bit(bits, 8);

        for (var i = 9; i < 15; i++)
            modules[8, 14 - i] = Bit(bits, i);

        for (var i = 0; i < 8; i++)
            modules[8, size - 1 - i] = Bit(bits, i);

        for (var i = 8; i < 15; i++)
            modules[size - 15 + i, 8] = Bit(bits, i);

        // The dark module, which is set in every code ever made.
        modules[size - 8, 8] = true;
    }

    private static bool Bit(int value, int at) => (value >> at & 1) != 0;

    /// <summary>The four penalties the specification scores a masked code by.</summary>
    private static int Penalty(bool[,] modules, int size)
    {
        var penalty = 0;
        var dark = 0;

        for (var line = 0; line < size; line++)
        {
            penalty += Line(modules, size, line, rows: true);
            penalty += Line(modules, size, line, rows: false);
        }

        // Every two-by-two block of one color.
        for (var row = 0; row < size - 1; row++)
        for (var column = 0; column < size - 1; column++)
            if (modules[row, column] == modules[row, column + 1]
                && modules[row, column] == modules[row + 1, column]
                && modules[row, column] == modules[row + 1, column + 1])
                penalty += 3;

        foreach (var module in modules)
            if (module)
                dark++;

        // And how far the proportion of dark modules strays from half, in steps of five percent.
        return penalty + Math.Abs(dark * 100 / (size * size) - 50) / 5 * 10;
    }

    /// <summary>
    /// One line's penalties: five or more of a color running together, and a run a
    /// reader could take for a finder pattern.
    /// </summary>
    private static int Line(bool[,] modules, int size, int line, bool rows)
    {
        var penalty = 0;
        var run = 1;
        var window = 0;

        for (var i = 0; i < size; i++)
        {
            var here = rows ? modules[line, i] : modules[i, line];

            if (i > 0)
            {
                var before = rows ? modules[line, i - 1] : modules[i - 1, line];

                if (here == before)
                {
                    run++;
                }
                else
                {
                    if (run >= 5) penalty += run - 2;
                    run = 1;
                }
            }

            // The finder's own ratio with four light modules to one side of it,
            // which is what a reader looks for and must not find here.
            window = (window << 1 | (here ? 1 : 0)) & 0x7FF;

            if (i >= 10 && window is 0b10111010000 or 0b00001011101)
                penalty += 40;
        }

        return penalty + (run >= 5 ? run - 2 : 0);
    }

    /// <summary>Draws the code as squares, as large as fits and centered.</summary>
    /// <remarks>
    /// Dark on light with the quiet border around it, whatever the theme is: a
    /// reader needs the contrast that way round and needs the border to find the
    /// edges at all.
    /// </remarks>
    public override void Render(DrawingContext context)
    {
        if (Text.Length == 0) return;

        var modules = Modules(Text);
        var count = modules.GetLength(0);
        var side = Math.Min(Bounds.Width, Bounds.Height);

        if (side <= 0) return;

        // A whole number of pixels to a module, so every square comes out the same size.
        var module = Math.Max(1, Math.Floor(side / (count + 8)));
        var quiet = module * 4;
        var drawn = module * count + quiet * 2;
        var left = (Bounds.Width - drawn) / 2;
        var top = (Bounds.Height - drawn) / 2;

        context.FillRectangle(Brushes.White, new Rect(left, top, drawn, drawn));

        for (var row = 0; row < count; row++)
        for (var column = 0; column < count; column++)
            if (modules[row, column])
                context.FillRectangle(
                    Brushes.Black,
                    new Rect(left + quiet + column * module, top + quiet + row * module, module, module));
    }
}
