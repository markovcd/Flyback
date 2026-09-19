namespace Flyback.Plugins.Picture;

/// <summary>
/// The letters a Text module draws: printable ASCII in five pixels by nine, drawn
/// by hand and kept here as the sheet it was drawn on.
/// </summary>
/// <remarks>
/// A font of its own rather than one read off the machine, because a patch has to
/// draw the same on every machine it is opened on and the engine may take nothing
/// that rasterises one (ADR-0019). Pixels rather than outlines because a pixel
/// is a square, and the distance to a union of squares is exact and cheap — see
/// <see cref="TextAtlas"/>.
/// <para>
/// Seven rows from the top of a capital to the baseline, and two below it for
/// the tails of g, j, p, q and y and the hook of a comma. Five columns, with a
/// sixth left empty between letters by whoever sets them.
/// </para>
/// <para>
/// A character outside the sheet is drawn as the last glyph on it, an empty box,
/// which is where DEL would have been: something typed that this font has no
/// letter for shows as a box rather than as nothing.
/// </para>
/// </remarks>
internal static class PixelFont
{
    /// <summary>Columns in a glyph.</summary>
    public const int Width = 5;

    /// <summary>Rows in a glyph, tails included.</summary>
    public const int Height = 9;

    /// <summary>Rows from the top of a capital to the baseline.</summary>
    public const int Cap = 7;

    /// <summary>Columns from one letter to the next: the glyph, and a gap.</summary>
    public const int Advance = Width + 1;

    private const char First = ' ';

    /// <summary>
    /// Whether the pixel at <paramref name="column"/> across and
    /// <paramref name="row"/> down from the top of a capital is inked.
    /// Outside the glyph is never inked.
    /// </summary>
    public static bool Inked(char letter, int column, int row) =>
        column is >= 0 and < Width
        && row is >= 0 and < Height
        && (Glyphs[Index(letter)] >> (row * Width + column) & 1UL) != 0;

    /// <summary>Whether this font has a letter for <paramref name="letter"/> rather than a box.</summary>
    public static bool Has(char letter) => letter >= First && letter < First + Glyphs.Length - 1;

    private static int Index(char letter) => Has(letter) ? letter - First : Glyphs.Length - 1;

    /// <summary>One number a glyph, a bit a pixel, row by row from the top left.</summary>
    private static readonly ulong[] Glyphs = Read(Sheets);

    private static ulong[] Read(string[] sheets)
    {
        var glyphs = new List<ulong>();

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

                glyphs.Add(bits);
            }
        }

        return [.. glyphs];
    }

    /// <summary>
    /// The font as it was drawn, sixteen letters to a sheet in the order ASCII
    /// numbers them, starting from a space.
    /// </summary>
    private static string[] Sheets =>
    [
        //       !     "     #     $     %     &     '     (     )     *     +     ,     -     .     /
        """
        ..... ..#.. .#.#. .#.#. ..#.. ##... .##.. ..#.. ...#. .#... ..... ..... ..... ..... ..... .....
        ..... ..#.. .#.#. .#.#. .#### ##..# #..#. ..#.. ..#.. ..#.. ..#.. ..#.. ..... ..... ..... ....#
        ..... ..#.. ..... ##### #.#.. ...#. #.#.. .#... .#... ...#. #.#.# ..#.. ..... ..... ..... ...#.
        ..... ..#.. ..... .#.#. .###. ..#.. .#... ..... .#... ...#. .###. ##### ..... ##### ..... ..#..
        ..... ..#.. ..... ##### ..#.# .#... #.#.# ..... .#... ...#. #.#.# ..#.. ..... ..... ..... .#...
        ..... ..... ..... .#.#. ####. #..## #..#. ..... ..#.. ..#.. ..#.. ..#.. .##.. ..... .##.. #....
        ..... ..#.. ..... .#.#. ..#.. ...## .##.# ..... ...#. .#... ..... ..... .##.. ..... .##.. .....
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..#.. ..... ..... .....
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... .#... ..... ..... .....
        """,
        // 0     1     2     3     4     5     6     7     8     9     :     ;     <     =     >     ?
        """
        .###. ..#.. .###. ##### ...#. ##### ..##. ##### .###. .###. ..... ..... ...#. ..... .#... .###.
        #...# .##.. #...# ...#. ..##. #.... .#... ....# #...# #...# .##.. .##.. ..#.. ..... ..#.. #...#
        #..## ..#.. ....# ..#.. .#.#. ####. #.... ...#. #...# #...# .##.. .##.. .#... ##### ...#. ....#
        #.#.# ..#.. ...#. ...#. #..#. ....# ####. ..#.. .###. .#### ..... ..... #.... ..... ....# ...#.
        ##..# ..#.. ..#.. ....# ##### ....# #...# .#... #...# ....# .##.. ..... .#... ##### ...#. ..#..
        #...# ..#.. .#... #...# ...#. #...# #...# .#... #...# ...#. .##.. .##.. ..#.. ..... ..#.. .....
        .###. .###. ##### .###. ...#. .###. .###. .#... .###. .##.. ..... .##.. ...#. ..... .#... ..#..
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..#.. ..... ..... ..... .....
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... .#... ..... ..... ..... .....
        """,
        // @     A     B     C     D     E     F     G     H     I     J     K     L     M     N     O
        """
        .###. .###. ####. .###. ###.. ##### ##### .###. #...# .###. ..### #...# #.... #...# #...# .###.
        #...# #...# #...# #...# #..#. #.... #.... #...# #...# ..#.. ...#. #..#. #.... ##.## #...# #...#
        ....# #...# #...# #.... #...# #.... #.... #.... #...# ..#.. ...#. #.#.. #.... #.#.# ##..# #...#
        .##.# #...# ####. #.... #...# ####. ####. #.### ##### ..#.. ...#. ##... #.... #.#.# #.#.# #...#
        #.#.# ##### #...# #.... #...# #.... #.... #...# #...# ..#.. ...#. #.#.. #.... #...# #..## #...#
        #.#.# #...# #...# #...# #..#. #.... #.... #...# #...# ..#.. #..#. #..#. #.... #...# #...# #...#
        .###. #...# ####. .###. ###.. ##### #.... .#### #...# .###. .##.. #...# ##### #...# #...# .###.
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... .....
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... .....
        """,
        // P     Q     R     S     T     U     V     W     X     Y     Z     [     \     ]     ^     _
        """
        ####. .###. ####. .#### ##### #...# #...# #...# #...# #...# ##### .###. ..... .###. ..#.. .....
        #...# #...# #...# #.... ..#.. #...# #...# #...# #...# #...# ....# .#... #.... ...#. .#.#. .....
        #...# #...# #...# #.... ..#.. #...# #...# #...# .#.#. #...# ...#. .#... .#... ...#. #...# .....
        ####. #...# ####. .###. ..#.. #...# #...# #.#.# ..#.. .#.#. ..#.. .#... ..#.. ...#. ..... .....
        #.... #.#.# #.#.. ....# ..#.. #...# #...# #.#.# .#.#. ..#.. .#... .#... ...#. ...#. ..... .....
        #.... #..#. #..#. ....# ..#.. #...# .#.#. #.#.# #...# ..#.. #.... .#... ....# ...#. ..... .....
        #.... .##.# #...# ####. ..#.. .###. ..#.. .#.#. #...# ..#.. ##### .###. ..... .###. ..... .....
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... #####
        ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... ..... .....
        """,
        // `     a     b     c     d     e     f     g     h     i     j     k     l     m     n     o
        """
        .#... ..... #.... ..... ....# ..... ..##. ..... #.... ..#.. ...#. #.... .##.. ..... ..... .....
        ..#.. ..... #.... ..... ....# ..... .#..# ..... #.... ..... ..... #.... ..#.. ..... ..... .....
        ...#. .###. #.##. .###. .##.# .###. .#... .#### #.##. .##.. ..##. #..#. ..#.. ##.#. #.##. .###.
        ..... ....# ##..# #.... #..## #...# ###.. #...# ##..# ..#.. ...#. #.#.. ..#.. #.#.# ##..# #...#
        ..... .#### #...# #.... #...# ##### .#... #...# #...# ..#.. ...#. ##... ..#.. #.#.# #...# #...#
        ..... #...# #...# #...# #...# #.... .#... #...# #...# ..#.. ...#. #.#.. ..#.. #...# #...# #...#
        ..... .#### ####. .###. .#### .###. .#... .#### #...# .###. ...#. #..#. .###. #...# #...# .###.
        ..... ..... ..... ..... ..... ..... ..... ....# ..... ..... #..#. ..... ..... ..... ..... .....
        ..... ..... ..... ..... ..... ..... ..... .###. ..... ..... .##.. ..... ..... ..... ..... .....
        """,
        // p     q     r     s     t     u     v     w     x     y     z     {     |     }     ~     (?)
        """
        ..... ..... ..... ..... .#... ..... ..... ..... ..... ..... ..... ...#. ..#.. .#... ..... #####
        ..... ..... ..... ..... .#... ..... ..... ..... ..... ..... ..... ..#.. ..#.. ..#.. ..... #...#
        ####. .#### #.##. .#### ###.. #...# #...# #...# #...# #...# ##### ..#.. ..#.. ..#.. .#... #...#
        #...# #...# ##..# #.... .#... #...# #...# #...# .#.#. #...# ...#. .#... ..#.. ...#. #.#.# #...#
        #...# #...# #.... .###. .#... #...# #...# #.#.# ..#.. #...# ..#.. ..#.. ..#.. ..#.. ...#. #...#
        #...# #...# #.... ....# .#..# #..## .#.#. #.#.# .#.#. #...# .#... ..#.. ..#.. ..#.. ..... #...#
        ####. .#### #.... ####. ..##. .##.# ..#.. .#.#. #...# .#### ##### ...#. ..#.. .#... ..... #####
        #.... ....# ..... ..... ..... ..... ..... ..... ..... ....# ..... ..... ..... ..... ..... .....
        #.... ....# ..... ..... ..... ..... ..... ..... ..... .###. ..... ..... ..... ..... ..... .....
        """,
    ];
}
