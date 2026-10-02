namespace Flyback.Plugins.Picture;

/// <summary>
/// A font a Text module draws in: letters drawn by hand a pixel at a time, and
/// kept in the source as the sheets they were drawn on.
/// </summary>
/// <remarks>
/// Fonts of its own rather than ones read off the machine, because a patch has to
/// draw the same on every machine it is opened on and the engine may take nothing
/// that rasterises one (ADR-0019). Pixels rather than outlines because a pixel is
/// a square, and the distance to a union of squares is exact and cheap — see
/// <see cref="TextAtlas"/>.
/// <para>
/// Each font names the characters its sheets hold, in the order they were drawn,
/// and ends on an empty box: something typed that the font has no letter for is
/// drawn as the box rather than as nothing, so it is seen to be there. A font of
/// one case draws a small letter as its capital.
/// </para>
/// <para>
/// A letter is advanced by its width and one empty column, and the advance is
/// even, so a short line centered under a long one lands on whole pixels.
/// </para>
/// </remarks>
internal sealed partial class BitmapFont
{
    private readonly ulong[] glyphs;
    private readonly Dictionary<char, int> index;

    private BitmapFont(
        string id,
        string name,
        int width,
        int height,
        int cap,
        bool oneCase,
        string characters,
        string[] sheets)
    {
        if ((width + 1) % 2 != 0) throw new ArgumentException("A letter's advance has to be even.", nameof(width));
        if (width * height > 64) throw new ArgumentException("A glyph has to fit in 64 bits.", nameof(height));

        Id = id;
        Name = name;
        Width = width;
        Height = height;
        Cap = cap;
        OneCase = oneCase;

        glyphs = Read(sheets);

        if (glyphs.Length != characters.Length + 1)
            throw new ArgumentException($"{name} draws {glyphs.Length} glyphs for {characters.Length} characters and a box.");

        index = characters.Select((c, i) => (c, i)).ToDictionary(pair => pair.c, pair => pair.i);
    }

    /// <summary>Every font there is, the first being what a Text is dropped with.</summary>
    public static IReadOnlyList<BitmapFont> All => [Pixel, Tiny];

    /// <summary>The font an id names, or null where there is none by that name.</summary>
    public static BitmapFont? Find(string id) => All.FirstOrDefault(font => font.Id == id);

    /// <summary>What a patch stores to choose this font. Stable: it is in every saved patch.</summary>
    public string Id { get; }

    /// <summary>What the panel's list calls it.</summary>
    public string Name { get; }

    /// <summary>Columns in a glyph.</summary>
    public int Width { get; }

    /// <summary>Rows in a glyph, tails included.</summary>
    public int Height { get; }

    /// <summary>Rows from the top of a capital to the baseline.</summary>
    public int Cap { get; }

    /// <summary>Whether a small letter is drawn as its capital.</summary>
    public bool OneCase { get; }

    /// <summary>Columns from one letter to the next: the glyph, and a gap.</summary>
    public int Advance => Width + 1;

    /// <summary>
    /// Whether the pixel at <paramref name="column"/> across and
    /// <paramref name="row"/> down from the top of a capital is inked.
    /// Outside the glyph is never inked.
    /// </summary>
    public bool Inked(char letter, int column, int row) =>
        column >= 0 && column < Width
        && row >= 0 && row < Height
        && (glyphs[Index(letter)] >> (row * Width + column) & 1UL) != 0;

    private int Index(char letter) =>
        index.TryGetValue(letter, out var at) ? at
            : OneCase && index.TryGetValue(char.ToUpperInvariant(letter), out var capital) ? capital
            : glyphs.Length - 1;

    /// <summary>One number a glyph, a bit a pixel, row by row from the top left.</summary>
    private ulong[] Read(string[] sheets)
    {
        var read = new List<ulong>();

        foreach (var sheet in sheets)
        {
            var rows = sheet.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var count = (rows[0].Length + 1) / Advance;

            for (var glyph = 0; glyph < count; glyph++)
            {
                var bits = 0UL;

                for (var row = 0; row < Height; row++)
                    for (var column = 0; column < Width; column++)
                        if (rows[row][glyph * Advance + column] == '#')
                            bits |= 1UL << (row * Width + column);

                read.Add(bits);
            }
        }

        return [.. read];
    }
}
