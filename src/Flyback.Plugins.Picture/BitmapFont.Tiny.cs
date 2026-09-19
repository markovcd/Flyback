namespace Flyback.Plugins.Picture;

internal sealed partial class BitmapFont
{
    /// <summary>
    /// Three pixels by five, in one case: the capitals, the digits and the
    /// punctuation of ASCII, with a row below the baseline for the hook of a
    /// comma. Small letters are drawn as capitals, as a font this small always
    /// has done.
    /// </summary>
    /// <remarks>
    /// The same size on the panel as Pixel, since 'size' is the height of a
    /// capital: each of its pixels is larger, so it reads as blocky rather than
    /// as small.
    /// </remarks>
    public static BitmapFont Tiny { get; } = new(
        "tiny",
        "Tiny",
        width: 3,
        height: 6,
        cap: 5,
        oneCase: true,
        characters: string.Concat(Enumerable.Range(' ', '_' - ' ' + 1).Select(c => (char)c)) + "`{|}~",
        TinySheets);

    /// <summary>
    /// The font as it was drawn: sixteen letters to a sheet, a space to an
    /// underscore in the order ASCII numbers them, then the five it keeps of the
    /// rest and the box.
    /// </summary>
    private static string[] TinySheets =>
    [
        //     !   "   #   $   %   &   '   (   )   *   +   ,   -   .   /
        """
        ... .#. #.# #.# .## #.# .#. .#. .#. .#. ... ... ... ... ... ..#
        ... .#. #.# ### ##. ..# #.# .#. #.. ..# #.# .#. ... ... ... ..#
        ... .#. ... #.# .#. .#. .#. ... #.. ..# .#. ### ... ### ... .#.
        ... ... ... ### .## #.. #.# ... #.. ..# #.# .#. ... ... ... #..
        ... .#. ... #.# ##. #.# .## ... .#. .#. ... ... .#. ... .#. #..
        ... ... ... ... ... ... ... ... ... ... ... ... #.. ... ... ...
        """,
        // 0   1   2   3   4   5   6   7   8   9   :   ;   <   =   >   ?
        """
        ### .#. ### ### #.# ### ### ### ### ### ... ... ..# ... #.. ###
        #.# ##. ..# ..# #.# #.. #.. ..# #.# #.# .#. .#. .#. ### .#. ..#
        #.# .#. ### .## ### ### ### ..# ### ### ... ... #.. ... ..# .##
        #.# .#. #.. ..# ..# ..# #.# .#. #.# ..# .#. ... .#. ### .#. ...
        ### ### ### ### ..# ### ### .#. ### ### ... .#. ..# ... #.. .#.
        ... ... ... ... ... ... ... ... ... ... ... #.. ... ... ... ...
        """,
        // @   A   B   C   D   E   F   G   H   I   J   K   L   M   N   O
        """
        .#. ### ##. .## ##. ### ### .## #.# ### ..# #.# #.. #.# ##. ###
        #.# #.# #.# #.. #.# #.. #.. #.. #.# .#. ..# #.# #.. ### #.# #.#
        #.# ### ##. #.. #.# ##. ##. #.# ### .#. ..# ##. #.. ### #.# #.#
        #.. #.# #.# #.. #.# #.. #.. #.# #.# .#. #.# #.# #.. #.# #.# #.#
        .## #.# ##. .## ##. ### #.. .## #.# ### .#. #.# ### #.# #.# ###
        ... ... ... ... ... ... ... ... ... ... ... ... ... ... ... ...
        """,
        // P   Q   R   S   T   U   V   W   X   Y   Z   [   \   ]   ^   _
        """
        ##. ### ##. .## ### #.# #.# #.# #.# #.# ### ##. #.. .## .#. ...
        #.# #.# #.# #.. .#. #.# #.# #.# #.# #.# ..# #.. #.. ..# #.# ...
        ##. #.# ##. .#. .#. #.# #.# ### .#. .#. .#. #.. .#. ..# ... ...
        #.. ### #.# ..# .#. #.# .#. ### #.# .#. #.. #.. ..# ..# ... ...
        #.. ..# #.# ##. .#. .## .#. #.# #.# .#. ### ##. ..# .## ... ###
        ... ... ... ... ... ... ... ... ... ... ... ... ... ... ... ...
        """,
        // `   {   |   }   ~   (?)
        """
        #.. .## .#. ##. ... ###
        .#. .#. .#. .#. #.. #.#
        ... ##. .#. .## ### #.#
        ... .#. .#. .#. ..# #.#
        ... .## .#. ##. ... ###
        ... ... ... ... ... ...
        """,
    ];
}
