using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Draws one module of each background a plugin may ask for and writes it out,
/// which is where the plugin guide's pictures come from.
/// </summary>
/// <remarks>
/// Run by hand, with SHOT_DIR naming somewhere to write to; skipped otherwise,
/// so an ordinary test run neither writes files nor fails for want of a folder.
/// The guide shows what the canvas draws rather than a drawing of it, and this
/// is what keeps that true: the shots are retaken from the same code the app
/// paints with.
/// </remarks>
public class SkinShotTests : UiTest
{
    /// <summary>
    /// ADR-0118 counts a picture's transparency as the node gray behind it. That
    /// is true of the bands a skinned module's ink is worked out from; it was not
    /// true of what the canvas actually drew, which left a transparent picture
    /// showing the canvas through it instead.
    /// </summary>
    [AvaloniaFact]
    public void A_transparent_picture_is_backed_by_the_node_gray()
    {
        var empty = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"></svg>""");

        var was = NodeCatalog.Current;

        var def = new NodeDef(
            "shot.clear", "Clear", ModuleCategories.Maths,
            [new PortSpec("in", PortKind.Any)],
            [new PortSpec("out", PortKind.Any)],
            (em, i) => [i[0]])
        { Skin = new ModuleSkin.Artwork(empty) };

        var added = was.With(new ModuleProvider("shot", "Shot"), [def]);

        NodeCatalog.Install(added.Catalog);

        try
        {
            var builder = new PatchBuilder(added.Catalog);
            var node = builder.Add(def.TypeId, Across, Down);

            var editor = NewCanvas(Wide, Tall);
            var window = Show(editor, Wide);

            editor.History.Open(builder.Patch);
            Settle(window);
            editor.View.FrameAll();
            Settle(window);

            var bounds = Geometry.Bounds(node, def);

            // Low in the body and left of the mark, clear of the header and of
            // any label — an empty svg leaves the whole block transparent, so
            // this is nothing but whatever the canvas painted underneath it.
            var probe = new Point(bounds.X + 6, bounds.Bottom - 6);
            var at = editor.TranslatePoint(editor.GraphToScreen.Transform(probe), window)
                ?? throw new InvalidOperationException("the editor is not in this window");

            var pixel = Pixel(window, at);

            Math.Abs(pixel.R - Colors.Node.R).ShouldBeLessThanOrEqualTo(4, $"was {pixel}");
            Math.Abs(pixel.G - Colors.Node.G).ShouldBeLessThanOrEqualTo(4, $"was {pixel}");
            Math.Abs(pixel.B - Colors.Node.B).ShouldBeLessThanOrEqualTo(4, $"was {pixel}");
        }
        finally
        {
            NodeCatalog.Install(was);
        }
    }

    /// <summary>The color the window actually drew at <paramref name="at"/>.</summary>
    private static Color Pixel(Window window, Point at)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the window rendered nothing");

        using var locked = frame.Lock();

        var x = (int)Math.Round(at.X);
        var y = (int)Math.Round(at.Y);

        var bytes = new byte[4];
        Marshal.Copy(locked.Address + y * locked.RowBytes + x * 4, bytes, 0, 4);

        return locked.Format == PixelFormat.Bgra8888
            ? Color.FromRgb(bytes[2], bytes[1], bytes[0])
            : Color.FromRgb(bytes[0], bytes[1], bytes[2]);
    }

    private static string? Where => Environment.GetEnvironmentVariable("SHOT_DIR");

    [AvaloniaFact]
    public void Draw_one_module_of_each_background()
    {
        if (Where is not { } folder) return;

        Directory.CreateDirectory(folder);

        var teal = new Swatch(0x2E, 0x8B, 0x57);
        var pale = new Swatch(0xF2, 0xE0, 0x7A);
        var violet = new Swatch(0x7A, 0x4A, 0xC8);

        var svg = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 196 74"><defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ff8a3c"/><stop offset="1" stop-color="#2a1050"/></linearGradient></defs><rect width="196" height="74" fill="url(#g)"/><circle cx="112" cy="22" r="12" fill="#ffe9a0"/><path d="M0,60 L40,40 L80,56 L120,34 L160,50 L196,38 L196,74 L0,74 Z" fill="#160a2e"/></svg>""");

        Shoot(folder, "palette", "Palette", new ModuleSkin.Palette(teal)
        {
            Glyph = "M4,12 A8,8 0 1,1 20,12 A8,8 0 1,1 4,12 M9,12 A3,3 0 1,1 15,12 A3,3 0 1,1 9,12",
        });

        Shoot(folder, "floor", "Palette, with a floor", new ModuleSkin.Palette(teal)
        {
            Floor = new Swatch(0x10, 0x20, 0x30),
        });

        Shoot(folder, "hatched", "Grain, hatched", new ModuleSkin.Grain(teal, GrainCut.Hatched));

        Shoot(folder, "milled", "Grain, milled", new ModuleSkin.Grain(pale, GrainCut.Milled)
        {
            ContrastText = true,
        });

        Shoot(folder, "beaded", "Grain, beaded", new ModuleSkin.Grain(violet, GrainCut.Beaded));

        Shoot(folder, "artwork", "Artwork", new ModuleSkin.Artwork(svg) { ContrastText = true });

        // And the same module with nothing said about it, for the guide to set
        // the other six against.
        Shoot(folder, "category", "No skin", null);
    }

    /// <summary>
    /// Where the one module stands: right above the Output a fresh patch comes
    /// with, so the two of them together are a small enough region for the fit to
    /// reach its own 1.4 ceiling. Put anywhere else, the view shrinks to hold
    /// both and the module comes out too small to read.
    /// </summary>
    private const double Across = 1100, Down = 60;

    /// <summary>
    /// The view. Wider and taller than the fit needs, and inside what a window
    /// will actually be given — asking for more height than the screen has ends
    /// with the window laid out again after the frame is captured.
    /// </summary>
    private const double Wide = 1400, Tall = 900;

    private void Shoot(string folder, string name, string title, ModuleSkin? skin)
    {
        var was = NodeCatalog.Current;

        var def = new NodeDef(
            "shot." + name, title, ModuleCategories.Maths,
            [new PortSpec("in", PortKind.Any), new PortSpec("level", PortKind.Scalar, 0.5f, 0f, 1f)],
            [new PortSpec("out", PortKind.Any)],
            (em, i) => [em.Mul(i[0], i[1])])
        { Skin = skin };

        var added = was.With(new ModuleProvider("shot", "Shot"), [def]);

        NodeCatalog.Install(added.Catalog);

        try
        {
            var builder = new PatchBuilder(added.Catalog);
            builder.Add(def.TypeId, Across, Down);

            // Large enough that the fit reaches its own 1.4 ceiling rather than
            // shrinking to hold the patch: a fresh patch has an Output in it as
            // well, and the view frames both.
            var editor = NewCanvas(Wide, Tall);
            var window = Show(editor, Wide);

            editor.History.Open(builder.Patch);
            Settle(window);

            // Again now the control has a size. The fit that runs when the patch
            // arrives sees whatever bounds the layout had got to, which for a
            // window still being laid out is not the ones it ends up with.
            editor.View.FrameAll();
            Settle(window);

            // Read off the transform rather than hunted for in the pixels, and
            // read before the capture rather than after it: a window still
            // settling its size fits the view again, and a transform read on the
            // far side of that describes a frame that no longer exists.
            var bounds = new Rect(Across, Down, NodeGeometry.Width, Geometry.Height(def))
                .TransformToAABB(editor.GraphToScreen)
                .Inflate(10);

            using var frame = window.CaptureRenderedFrame()!;

            if (!new Rect(frame.Size).Contains(bounds))
                throw new InvalidOperationException($"{name} is off the {frame.Size} frame: {bounds}");

            using var whole = new Bitmap(new MemoryStream(Save(frame)));

            Crop(whole, bounds, Path.Combine(folder, "skin-" + name + ".png"));
        }
        finally
        {
            NodeCatalog.Install(was);
        }
    }

    private static byte[] Save(Bitmap frame)
    {
        using var stream = new MemoryStream();

        frame.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    private static void Crop(Bitmap whole, Rect to, string path)
    {
        var size = new PixelSize((int)to.Width, (int)to.Height);

        using var target = new RenderTargetBitmap(size);
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(whole, to, new Rect(size.ToSize(1)));
        }

        target.Save(path, new PngBitmapEncoderOptions());
    }
}
