using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What is behind one module, asked what color it is at a given height.
/// </summary>
/// <remarks>
/// The question a wash and a picture both have to answer, because it is what
/// text over either is colored against — see <see cref="ModuleSkin.ContrastText"/>.
/// A wash answers it exactly, being two stops and a lerp; a picture answers it
/// from the bands it was read in.
/// </remarks>
internal readonly struct ModuleBackdrop
{
    private readonly Color headerTop, headerFloor, bodyTop, bodyFloor;

    private ModuleBackdrop(Color headerTop, Color headerFloor, Color bodyTop, Color bodyFloor, ModuleArtwork? picture)
    {
        this.headerTop = headerTop;
        this.headerFloor = headerFloor;
        this.bodyTop = bodyTop;
        this.bodyFloor = bodyFloor;
        Picture = picture;
    }

    /// <summary>The picture behind the module, or null where it is painted instead.</summary>
    public ModuleArtwork? Picture { get; }

    /// <param name="panel">
    /// The panel's own picture rather than the block's — see
    /// <see cref="ModuleArtwork.Of"/>. The canvas never asks for this; it is
    /// the plate reading the same background the wash beside it is drawing.
    /// </param>
    public static ModuleBackdrop Of(NodeDef def, bool selected, bool panel = false)
    {
        var picture = ModuleSkins.Of(def) is ModuleSkin.Artwork artwork ? ModuleArtwork.Of(artwork, panel) : null;
        var (accent, floor) = Colors.Palette(def);

        return new ModuleBackdrop(
            NodeSkin.HeaderTop(accent, selected),
            NodeSkin.HeaderFloor(floor),
            NodeSkin.BodyTop(accent, selected),
            NodeSkin.BodyFloor(floor, selected),
            picture);
    }

    /// <summary>
    /// The header's own gradient — or, behind a picture, its topmost band stood
    /// in for both ends, so a name reads against what is actually near the top
    /// of the picture rather than the whole of it averaged together.
    /// </summary>
    public (Color Top, Color Floor) HeaderGradient =>
        Picture is { } picture ? (picture.Band(0), picture.Band(0)) : (headerTop, headerFloor);

    /// <summary>Which way the header's own text is lifted — the header half of <see cref="Lift"/>.</summary>
    public bool HeaderLift => Picture is { } picture
        ? !Colors.Light(picture.Mean)
        : !Colors.Light(Colors.Blend(headerTop, headerFloor, 0.5));

    /// <summary>
    /// The body's own top color — or, behind a picture, its topmost band again —
    /// which is what a line standing on the body just under the header reads
    /// against.
    /// </summary>
    public Color BodyInk => Picture is { } picture ? picture.Band(0) : bodyTop;

    /// <summary>Which way that line is lifted — the body half of <see cref="Lift"/>.</summary>
    public bool BodyLift => Picture is { } picture ? !Colors.Light(picture.Mean) : !Colors.Light(bodyTop);

    /// <summary>The color behind the point <paramref name="y"/> down the module.</summary>
    public Color At(Rect bounds, double y)
    {
        if (Picture is { } picture) return picture.Band(Down(bounds, y));

        var header = y - bounds.Y;

        return header < NodeGeometry.HeaderHeight
            ? Colors.Blend(headerTop, headerFloor, Math.Clamp(header / NodeGeometry.HeaderHeight, 0, 1))
            : Colors.Blend(bodyTop, bodyFloor, Down(bounds, y));
    }

    /// <summary>
    /// Whether text at <paramref name="y"/> reads better light than dark.
    /// </summary>
    /// <remarks>
    /// Taken from the whole surface the line of text sits on rather than from the
    /// line itself, so every label down a body agrees and a word is never dark at
    /// one end and light at the other. A painted module has two surfaces — a pale
    /// accent makes a pale header band over a body that is nearly all node grey,
    /// and the title and the labels then want opposite answers.
    /// </remarks>
    public bool Lift(Rect bounds, double y)
    {
        if (Picture is { } picture) return !Colors.Light(picture.Mean);

        return y - bounds.Y < NodeGeometry.HeaderHeight
            ? !Colors.Light(Colors.Blend(headerTop, headerFloor, 0.5))
            : !Colors.Light(Colors.Blend(bodyTop, bodyFloor, 0.5));
    }

    private static double Down(Rect bounds, double y) =>
        bounds.Height <= 0 ? 0 : Math.Clamp((y - bounds.Y) / bounds.Height, 0, 1);
}
