using Flyback.Core.Compile;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Lines of text drawn into one picture, a band to a line, for a Text module to
/// read: the distance to the letters in red, and when each letter appears in
/// green.
/// </summary>
/// <remarks>
/// A picture because a program cannot hold a string and cannot index a list: the
/// one thing it can look something up in by a number it computed is a texture,
/// and <see cref="OpCode.SamplePicture"/> is that on both backends. So which line
/// shows is where the picture is read, and choosing it costs one multiply rather
/// than a window per line the way a sequencer's step does.
/// <para>
/// Distance rather than ink, because the texture is eight bits a channel and
/// filtered: ink stretched to a whole frame would be a blur, where a distance
/// filtered between two texels is still a distance and its zero is still a
/// sharp edge. It is exact rather than estimated — a glyph is a union of squares,
/// and the distance to a square has a formula — and good to <see cref="Reach"/>
/// font pixels either side of an edge, past which it is held.
/// </para>
/// <para>
/// Green is the share of the line that has to be revealed before the letter
/// nearest this texel shows, so a typewriter is one comparison. Nearest rather
/// than the cell the texel is in, so a hidden letter's edge never shows in the
/// gap beside the letter before it.
/// </para>
/// </remarks>
/// <param name="Image">The bands, top to bottom.</param>
/// <param name="Font">What the letters are drawn in.</param>
/// <param name="Lines">How many bands there are.</param>
/// <param name="Columns">How many letters the longest line holds, which every band is as wide as.</param>
/// <param name="Cut">Whether lines or letters past what one atlas holds were left out.</param>
internal sealed record TextAtlas(LoadedImage Image, BitmapFont Font, int Lines, int Columns, bool Cut)
{
    /// <summary>Texels to a font pixel.</summary>
    public const int Texels = 4;

    /// <summary>Font pixels of nothing around a line's ink, on every side.</summary>
    public const int Margin = 1;

    /// <summary>How far from an edge the distance is written before it is held, in font pixels.</summary>
    public const float Reach = 4f;

    /// <summary>The most lines one atlas holds.</summary>
    public const int MostLines = 64;

    /// <summary>The most letters one line holds.</summary>
    public const int MostColumns = 64;

    /// <summary>A band's height in font pixels: the glyph, and a margin above and below.</summary>
    public int Band => Font.Height + 2 * Margin;

    /// <summary>How wide the widest line's ink is, in font pixels.</summary>
    public int Ink => Columns * Font.Advance - 1;

    private static readonly Dictionary<(string Font, string Text), TextAtlas?> Baked = [];

    /// <summary>
    /// The atlas for <paramref name="text"/> in <paramref name="font"/>, one line
    /// to each line break, or null where there is nothing to draw.
    /// </summary>
    /// <remarks>
    /// The same instance for the same text in the same font, for as long as it is asked for:
    /// every edit recompiles the patch, and the renderer keeps a texture for as
    /// long as it is handed the same picture — so a knob turned beside a Text
    /// bakes nothing and uploads nothing. The texts it remembers are forgotten
    /// together once there are more than a handful, which is a patch being typed
    /// into rather than one being played.
    /// </remarks>
    public static TextAtlas? Of(BitmapFont font, string text)
    {
        lock (Baked)
        {
            if (Baked.TryGetValue((font.Id, text), out var known)) return known;

            if (Baked.Count >= 32) Baked.Clear();

            return Baked[(font.Id, text)] = Bake(font, text);
        }
    }

    private static TextAtlas? Bake(BitmapFont font, string text)
    {
        var lines = text.Split('\n').ToList();

        // A break at the very end is the box's last Enter rather than a page
        // somebody meant to leave blank.
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0 || lines.All(string.IsNullOrWhiteSpace)) return null;

        var cut = lines.Count > MostLines || lines.Any(line => line.Length > MostColumns);

        lines = [.. lines.Take(MostLines).Select(line => line.Length > MostColumns ? line[..MostColumns] : line)];

        var columns = Math.Max(1, lines.Max(line => line.Length));
        var across = columns * font.Advance + 1;

        var width = across * Texels;
        var band = font.Height + 2 * Margin;
        var height = lines.Count * band * Texels;
        var pixels = new float[width * height * 3];

        Parallel.For(0, lines.Count, index =>
        {
            var line = lines[index];

            // Centred in the band by whole letters' worth of half-advances, which
            // is a whole number of font pixels since an advance is even.
            var indent = Margin + (columns - line.Length) * font.Advance / 2;

            // Which letter of the line inks each font pixel of the band, or -1.
            var owner = new int[band, across];

            for (var row = 0; row < band; row++)
                for (var column = 0; column < across; column++)
                {
                    owner[row, column] = -1;

                    var from = column - indent;
                    if (from < 0) continue;

                    var letter = from / font.Advance;
                    if (letter >= line.Length) continue;

                    if (font.Inked(line[letter], from % font.Advance, row - Margin))
                        owner[row, column] = letter;
                }

            for (var ty = 0; ty < band * Texels; ty++)
                for (var tx = 0; tx < width; tx++)
                {
                    var (distance, nearest) = Nearest(owner, (tx + 0.5) / Texels, (ty + 0.5) / Texels);

                    var at = (((index * band * Texels) + ty) * width + tx) * 3;

                    pixels[at + 0] = (float)(0.5 + distance / (2 * Reach));
                    pixels[at + 1] = nearest < 0 ? 0f : (nearest + 1f) / line.Length;
                    pixels[at + 2] = pixels[at + 0];
                }
        });

        return new TextAtlas(new LoadedImage(pixels, width, height), font, lines.Count, columns, cut);
    }

    /// <summary>
    /// The signed distance from a point to the inked squares, in font pixels and
    /// held to <see cref="Reach"/>, and which letter the edge it measured to
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// Outside the ink it is the distance to the nearest inked square; inside, to
    /// the nearest empty one, negated. Both are exact for a union of squares, and
    /// nothing past <see cref="Reach"/> is looked at, since it would be held there
    /// anyway. Anything off the band is empty.
    /// </remarks>
    private static (double Distance, int Letter) Nearest(int[,] owner, double x, double y)
    {
        var rows = owner.GetLength(0);
        var columns = owner.GetLength(1);

        var column = (int)Math.Floor(x);
        var row = (int)Math.Floor(y);

        int Owner(int r, int c) => r >= 0 && r < rows && c >= 0 && c < columns ? owner[r, c] : -1;

        var inside = Owner(row, column) >= 0;
        var reach = (int)Math.Ceiling(Reach) + 1;

        var best = (double)Reach;
        var letter = inside ? Owner(row, column) : -1;

        for (var r = row - reach; r <= row + reach; r++)
            for (var c = column - reach; c <= column + reach; c++)
            {
                var other = Owner(r, c);

                // Across the edge from where the point is: ink from outside,
                // nothing from inside.
                if ((other >= 0) == inside) continue;

                var dx = Math.Max(Math.Max(c - x, 0), x - (c + 1));
                var dy = Math.Max(Math.Max(r - y, 0), y - (r + 1));
                var distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance >= best) continue;

                best = distance;
                if (!inside) letter = other;
            }

        return (inside ? -best : best, letter);
    }
}
