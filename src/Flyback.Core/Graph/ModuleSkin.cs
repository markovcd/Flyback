namespace Flyback.Core.Graph;

/// <summary>
/// A color a plugin hands over, Core knowing no toolkit to name one with.
/// </summary>
public readonly record struct Swatch(byte Red, byte Green, byte Blue);

/// <summary>
/// The texture a <see cref="ModuleSkin.Grain"/> is cut with. A short list on
/// purpose: these are drawn by the shell, so a cut is a thing it knows how to
/// make rather than a thing a plugin describes.
/// </summary>
public enum GrainCut
{
    /// <summary>Diagonal rules, the coarsest of the three.</summary>
    Hatched,

    /// <summary>Fine upright rules, the knurl on a control.</summary>
    Milled,

    /// <summary>A grid of dots, the quietest.</summary>
    Beaded,
}

/// <summary>
/// What is behind a module, where its category's accent is not what its author
/// wants.
/// </summary>
/// <remarks>
/// The background and nothing else. The block is the same block: its shape, its
/// header, where its title sits and what its sockets are called are not a
/// plugin's to move, because a canvas of modules that each laid themselves out
/// differently would stop being a patch.
/// <para>
/// Described rather than drawn, the way <see cref="ExtraField"/> is (ADR-0055):
/// a plugin says what it wants behind its module and the App draws it, so no
/// plugin ships a control and no plugin binary is pinned to the Avalonia a
/// given build shipped.
/// </para>
/// </remarks>
public abstract record ModuleSkin
{
    /// <summary>
    /// Closed: the shell draws a skin by asking which of these it is, and a kind
    /// from outside would be a background nothing knows how to paint.
    /// </summary>
    private protected ModuleSkin() { }

    /// <summary>
    /// Whether the text on the module is worked out from the background behind
    /// it rather than drawn white.
    /// </summary>
    /// <remarks>
    /// Opt-in, because white is what the rest of the canvas is written in and a
    /// module that joins it should not be the one that differs. The case it is
    /// for is a pale, saturated or busy background, where white is unreadable
    /// and nothing the shell knows about the color can save it.
    /// </remarks>
    public bool ContrastText { get; init; }

    /// <summary>
    /// Drawn the way every built-in module is, from colors and a mark of the
    /// plugin's own: the wash down the body, the band across the header, and the
    /// glyph set faintly behind the labels.
    /// </summary>
    /// <param name="Accent">
    /// The one color everything is worked out from, standing where the category's
    /// accent would.
    /// </param>
    public record Palette(Swatch Accent) : ModuleSkin
    {
        /// <summary>
        /// What the wash falls to at the floor, or null to fall to the accent the
        /// way a built-in module does.
        /// </summary>
        public Swatch? Floor { get; init; }

        /// <summary>
        /// The mark across the body as SVG path data on a twenty-four unit box,
        /// or null to take the category's. Stroked, not filled — see
        /// <c>ModuleGlyphs</c>; path data that will not parse draws nothing.
        /// </summary>
        public string? Glyph { get; init; }
    }

    /// <summary>
    /// The same palette with a texture cut across it, tiled over the body and
    /// clipped to it.
    /// </summary>
    /// <remarks>
    /// A palette rather than a thing beside one: a grain is a second channel over
    /// the first, and everything a <see cref="Palette"/> says still holds. What it
    /// buys is a module that is still telling you whose it is in a grayscale
    /// screenshot, at a zoom where no mark resolves, and to somebody who cannot
    /// separate two hues — none of which a color alone survives.
    /// <para>
    /// Cut here rather than drawn by the plugin, because a texture is the one
    /// thing a background wants that is the same at every size: a named cut is
    /// scale-free where a picture of a hatch is not.
    /// </para>
    /// </remarks>
    public sealed record Grain(Swatch Accent, GrainCut Cut) : Palette(Accent);

    /// <summary>
    /// A picture behind the module: SVG, PNG or GIF, scaled to cover the body and
    /// clipped to it.
    /// </summary>
    /// <param name="Bytes">
    /// The file itself, which a plugin most often reads out of its own embedded
    /// resources. Bytes rather than a path, so a skin needs nothing deployed
    /// beside the assembly and nothing read off disk while the canvas paints.
    /// </param>
    public sealed record Artwork(ReadOnlyMemory<byte> Bytes) : ModuleSkin
    {
        /// <summary>
        /// Whether an animated GIF runs. Off holds it at its first frame, which is
        /// what a patch of forty modules wants and what anybody who finds a
        /// moving module distracting wants.
        /// </summary>
        public bool Animate { get; init; } = true;
    }
}
