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

    public static ModuleBackdrop Of(NodeDef def, bool selected)
    {
        var picture = ModuleSkins.Of(def) is ModuleSkin.Artwork artwork ? ModuleArtwork.Of(artwork) : null;
        var (accent, floor) = Colors.Palette(def);

        return new ModuleBackdrop(
            NodeSkin.HeaderTop(accent, selected),
            NodeSkin.HeaderFloor(floor),
            NodeSkin.BodyTop(accent, selected),
            NodeSkin.BodyFloor(floor, selected),
            picture);
    }

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
