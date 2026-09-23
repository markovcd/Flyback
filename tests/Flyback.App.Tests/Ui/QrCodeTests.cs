using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.App.Controls;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The QR code the About window and the website both show the donation address as.
/// </summary>
/// <remarks>
/// Read back rather than compared to a picture: the code is unmasked, walked down
/// the same zigzag and turned back into the string it was made from, so a change
/// that moved a module would have to move it consistently in both directions to
/// go unnoticed. What that does not cover — the error correction and the format
/// bits — was checked once by decoding a rendering of it with a reader that has
/// nothing to do with this code.
/// </remarks>
public class QrCodeTests : UiTest
{
    [AvaloniaFact]
    public void A_code_reads_back_as_the_text_it_was_made_from()
    {
        Decode(QrCode.Modules(About.BitcoinAddress)).ShouldBe(About.BitcoinAddress);
    }

    /// <summary>Short and long, so the version and the padding are both exercised.</summary>
    [AvaloniaTheory]
    [InlineData("a")]
    [InlineData("bitcoin")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")]
    public void Any_text_short_enough_reads_back(string text)
    {
        Decode(QrCode.Modules(text)).ShouldBe(text);
    }

    /// <summary>A bech32 address is 42 characters, which is a version 3 code exactly.</summary>
    [AvaloniaFact]
    public void A_donation_address_makes_a_version_three_code()
    {
        QrCode.Modules(About.BitcoinAddress).GetLength(0).ShouldBe(29);
    }

    [AvaloniaFact]
    public void Text_too_long_for_a_version_three_code_is_refused()
    {
        Should.Throw<ArgumentException>(() => QrCode.Modules(new string('x', 43)));
    }

    /// <summary>
    /// And what the control draws is what it encodes. The window's pixels come
    /// back, every module's middle is sampled, and the code read off the screen
    /// is the address again — which is the part a phone's camera depends on.
    /// </summary>
    [AvaloniaFact]
    public void What_it_draws_reads_back_as_the_address()
    {
        // Thirty-seven modules across counting the quiet border, eight pixels each.
        const int module = 8;
        const int quiet = 4;
        const int side = (29 + 2 * quiet) * module;

        var window = Show(new QrCode { Text = About.BitcoinAddress, Width = side, Height = side }, side);
        var drawn = Frame(window);
        var scale = drawn.GetLength(0) / (double)side;
        var modules = new bool[29, 29];

        for (var row = 0; row < 29; row++)
        for (var column = 0; column < 29; column++)
        {
            var x = (int)(((quiet + column) * module + module / 2) * scale);
            var y = (int)(((quiet + row) * module + module / 2) * scale);

            modules[row, column] = drawn[x, y];
        }

        Decode(modules).ShouldBe(About.BitcoinAddress);
    }

    /// <summary>What the window drew, true where the pixel came out dark.</summary>
    private static bool[,] Frame(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the window rendered nothing");

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var dark = new bool[locked.Size.Width, locked.Size.Height];
        var blue = locked.Format == PixelFormat.Bgra8888 ? 0 : 2;

        for (var y = 0; y < locked.Size.Height; y++)
        for (var x = 0; x < locked.Size.Width; x++)
        {
            var at = y * locked.RowBytes + x * 4;

            dark[x, y] = (bytes[at + blue] + bytes[at + 1] + bytes[at + 2 - blue]) / 3 < 128;
        }

        return dark;
    }

    /// <summary>
    /// Writes the website's copy of the code, which is the same encoder's output
    /// rather than a second drawing of the address.
    /// </summary>
    /// <remarks>
    /// Run by hand, with SHOT_DIR naming somewhere to write to; skipped otherwise,
    /// the way the site's other pictures are taken.
    /// </remarks>
    [AvaloniaFact]
    public void Draw_the_donation_code()
    {
        if (Environment.GetEnvironmentVariable("SHOT_DIR") is not { } folder) return;

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "donate-qr.svg"), Svg(QrCode.Modules(About.BitcoinAddress)));
    }

    /// <summary>
    /// The code as an SVG, a module to a unit, in the same ink, paper and rounding
    /// the control draws it in — the website's copy has to be the same picture.
    /// </summary>
    private static string Svg(bool[,] modules)
    {
        const int quiet = 4;

        var size = modules.GetLength(0);
        var box = size + 2 * quiet;
        var ink = Hex(QrCode.Ink);
        var paper = Hex(QrCode.Paper);
        var svg = new StringBuilder();

        string Block(string fill, double row, double column, double span, double radius) =>
            $"<rect fill=\"{fill}\" x=\"{Round(column + quiet)}\" y=\"{Round(row + quiet)}\""
            + $" width=\"{Round(span)}\" height=\"{Round(span)}\" rx=\"{Round(radius)}\"/>";

        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {box} {box}\">");
        svg.Append($"<rect fill=\"{paper}\" width=\"{box}\" height=\"{box}\" rx=\"2\"/>");

        foreach (var (row, column) in QrCode.Corners(size))
        {
            svg.Append(Block(ink, row, column, 7, 2));
            svg.Append(Block(paper, row + 1, column + 1, 5, 1.4));
            svg.Append(Block(ink, row + 2, column + 2, 3, 0.9));
        }

        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
            if (modules[row, column] && !QrCode.InFinder(size, row, column))
                svg.Append(Block(ink, row, column, 1, 0.25));

        return svg.Append("</svg>").ToString();
    }

    private static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";

    private static string Round(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a code the way a reader does: the mask comes out of the format bits,
    /// the data comes off the zigzag, and the header says how much of it is text.
    /// </summary>
    private static string Decode(bool[,] modules)
    {
        var size = modules.GetLength(0);
        var mask = Mask(modules, size);
        var reserved = Reserved(modules, size);
        var bits = new List<bool>();
        var upwards = true;

        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;

            for (var step = 0; step < size; step++)
            {
                var row = upwards ? size - 1 - step : step;

                for (var column = right; column > right - 2; column--)
                    if (!reserved[row, column])
                        bits.Add(modules[row, column] ^ Masked(row, column, mask));
            }

            upwards = !upwards;
        }

        Read(bits, 0, 4).ShouldBe(0b0100, "byte mode");

        var length = Read(bits, 4, 8);
        var text = new byte[length];

        for (var i = 0; i < length; i++)
            text[i] = (byte)Read(bits, 12 + 8 * i, 8);

        return Encoding.UTF8.GetString(text);
    }

    private static int Read(List<bool> bits, int at, int count)
    {
        var value = 0;

        for (var i = 0; i < count; i++)
            value = value << 1 | (bits[at + i] ? 1 : 0);

        return value;
    }

    /// <summary>Which mask the format bits name, undoing their BCH code to find out.</summary>
    private static int Mask(bool[,] modules, int size)
    {
        var bits = 0;

        for (var i = 0; i < 8; i++)
            bits |= (modules[8, size - 1 - i] ? 1 : 0) << i;

        for (var i = 8; i < 15; i++)
            bits |= (modules[size - 15 + i, 8] ? 1 : 0) << i;

        return (bits ^ 0x5412) >> 10 & 0b111;
    }

    /// <summary>
    /// Everything a reader knows the place of without reading it: the finders and
    /// their separators, the timing patterns, the alignment pattern and the format
    /// bits. The same places the encoder keeps clear, derived the same way.
    /// </summary>
    private static bool[,] Reserved(bool[,] modules, int size)
    {
        var reserved = new bool[size, size];

        (int Row, int Column)[] finders = [(0, 0), (0, size - 7), (size - 7, 0)];

        foreach (var (row, column) in finders)
            for (var y = -1; y <= 7; y++)
            for (var x = -1; x <= 7; x++)
            {
                var (r, c) = (row + y, column + x);

                if (r >= 0 && r < size && c >= 0 && c < size)
                    reserved[r, c] = true;
            }

        for (var i = 8; i < size - 8; i++)
            reserved[6, i] = reserved[i, 6] = true;

        if (size >= 25)
        {
            var center = size - 7;

            for (var y = -2; y <= 2; y++)
            for (var x = -2; x <= 2; x++)
                reserved[center + y, center + x] = true;
        }

        for (var i = 0; i < 9; i++)
            reserved[8, i] = reserved[i, 8] = true;

        for (var i = 0; i < 8; i++)
            reserved[8, size - 1 - i] = reserved[size - 1 - i, 8] = true;

        return reserved;
    }

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
}
