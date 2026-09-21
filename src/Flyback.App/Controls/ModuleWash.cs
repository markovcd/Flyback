using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The selected block's face behind the whole panel: the band its name stands on,
/// the wash under that, the grain cut across it or the picture its author hung
/// there, and its mark set large in the middle — all fading out together.
/// </summary>
/// <remarks>
/// Behind everything rather than under the plate alone, because a fill that stopped
/// where the plate does would end on a line — which is the thing the fade is for. It
/// is the panel's ground and never takes a click, and it does not scroll: what moves
/// under it is the reading.
/// </remarks>
internal sealed class ModuleWash : Control
{
    public ModuleWash()
    {
        Name = "module-wash";
        IsHitTestVisible = false;
    }

    /// <summary>
    /// How big the mark is drawn, against the twenty-four units it is laid out on.
    /// Far larger than the canvas draws one: there is one block on the panel where
    /// a patch has forty, and this is the whole of what says which at a glance.
    /// </summary>
    private const double MarkSize = 288;

    /// <summary>How far the mark keeps off the panel's edges.</summary>
    private const double MarkInset = 12;

    /// <summary>
    /// How faint the whole face is drawn for a module that is switched off —
    /// the same fade the canvas draws the block itself at (ADR-0117).
    /// </summary>
    private const double OffOpacity = 0.38;

    /// <summary>How often the panel repaints while its picture is animating — see <see cref="KeepMoving"/>.</summary>
    private const double Tick = 40;

    /// <summary>What an animated picture on the panel is read against — its own clock, since the panel is not the canvas.</summary>
    private static readonly Stopwatch clock = Stopwatch.StartNew();

    private IBrush? wash, band, grain;
    private ModuleArtwork? picture;
    private Geometry? glyph;
    private IPen? mark;
    private bool moving, ticking;

    /// <summary>
    /// How far down the panel the plate reaches, so the mark starts under it rather
    /// than behind the name.
    /// </summary>
    public double Below
    {
        get;
        set
        {
            if (Math.Abs(field - value) < 0.5) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// How deep the band behind the name runs — <see cref="ModulePlate.Band"/>. The
    /// band is drawn here rather than there so that it fades with everything else
    /// instead of ending on its own line.
    /// </summary>
    public double BandHeight
    {
        get;
        set
        {
            if (Math.Abs(field - value) < 0.5) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Whether the module shown is switched off, which fades the whole face —
    /// the panel's answer to ADR-0117, the way the canvas fades the block.
    /// </summary>
    public bool Off
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>Shows the module's own background, or nothing where none is wanted.</summary>
    public void Show(NodeDef? def)
    {
        if (def is null)
        {
            Clear();
            return;
        }

        var (accent, floor) = Colors.Palette(def);
        var skin = ModuleSkins.Of(def);

        wash = NodeSkin.Body(accent, floor, selected: false);
        grain = skin is ModuleSkin.Grain { Cut: var cut }
            ? NodeSkin.Cut(cut, NodeSkin.BodyTop(accent, selected: false))
            : null;
        picture = skin is ModuleSkin.Artwork artwork ? ModuleArtwork.Of(artwork, panel: true) : null;
        glyph = ModuleGlyphs.For(def);
        mark = NodeSkin.Mark(accent);

        // A picture is the background, so no band is laid over it: the name stands
        // on what the plugin hung there.
        band = picture is null ? NodeSkin.Header(accent, floor, selected: false) : null;

        InvalidateVisual();
    }

    /// <summary>The grays a box is drawn in, which belongs to no category.</summary>
    public void ShowBox()
    {
        wash = NodeSkin.Box(selected: false);
        band = NodeSkin.BoxHeader;
        grain = null;
        picture = null;
        glyph = ModuleGlyphs.Group;
        mark = NodeSkin.BoxMark;

        InvalidateVisual();
    }

    public void Clear()
    {
        wash = null;
        band = null;
        grain = null;
        picture = null;
        glyph = null;
        mark = null;
        Off = false;

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (wash is null && picture is null) return;

        var bounds = new Rect(Bounds.Size);

        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using (context.PushOpacity(Off ? OffOpacity : 1))
        {
            // One mask over the fill, so a picture, a wash and a grain all disappear
            // the same way.
            using (context.PushOpacityMask(NodeSkin.Fade(bounds), bounds))
            {
                if (picture is { } art)
                {
                    if (art.Paint(context, new RoundedRect(bounds), clock.Elapsed.TotalMilliseconds))
                        moving = true;
                }
                else
                {
                    context.FillRectangle(wash!, bounds);

                    if (grain is { } cut) context.FillRectangle(cut, bounds);
                }

                DrawBand(context, bounds);
            }

            // Outside the mask: the mark stands where the fill has already gone, and it
            // is the one thing here that has to be there at any panel height.
            DrawMark(context, bounds);
        }

        KeepMoving();
    }

    /// <summary>
    /// The band the name stands on, across the top of the panel and square: this is
    /// the head of the panel rather than a block on a canvas. Its relief is drawn
    /// even behind a picture — see <see cref="Render"/> — because the title bar
    /// reads the same way over artwork as it does over paint (ADR-0118).
    /// </summary>
    private void DrawBand(DrawingContext context, Rect bounds)
    {
        if (BandHeight <= 0) return;

        var top = new Rect(bounds.X, bounds.Y, bounds.Width, Math.Min(BandHeight, bounds.Height));

        if (band is not null) context.FillRectangle(band, top);

        NodeSkin.Relief(context, top);
    }

    /// <summary>
    /// Asks for another frame while the picture just drawn was animating, and
    /// only while the panel is actually on screen — an inspector nobody is
    /// looking at has no reason to keep a timer running.
    /// </summary>
    private void KeepMoving()
    {
        if (!moving || ticking || !IsEffectivelyVisible)
        {
            moving = false;
            return;
        }

        moving = false;
        ticking = true;

        DispatcherTimer.RunOnce(
            () =>
            {
                ticking = false;
                if (IsEffectivelyVisible) InvalidateVisual();
            },
            TimeSpan.FromMilliseconds(Tick));
    }

    /// <summary>
    /// The mark, against the right-hand edge and under the plate — always drawn,
    /// however little of the panel is left, because it is what the panel is about.
    /// </summary>
    /// <remarks>
    /// Except behind a picture, which is what the canvas does: the background is
    /// the author's there, and a category's mark laid over it would be the shell
    /// drawing on somebody else's artwork.
    /// </remarks>
    private void DrawMark(DrawingContext context, Rect bounds)
    {
        if (glyph is null || mark is null || picture is not null) return;

        var size = Math.Min(MarkSize, bounds.Width - MarkInset * 2);

        if (size <= 0) return;

        var scale = size / ModuleGlyphs.Box;
        var at = new Point(bounds.Right - MarkInset - size, Below + MarkInset);

        using (context.PushClip(bounds))
        using (context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            context.DrawGeometry(null, mark, glyph);
        }
    }
}
