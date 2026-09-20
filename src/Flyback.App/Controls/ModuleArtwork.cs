using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using Flyback.Core.Graph;
using SkiaSharp;

namespace Flyback.App.Controls;

/// <summary>
/// A decoded picture a plugin hung behind its module: what to draw, when each
/// frame gives way to the next, and how light it is down its own height.
/// </summary>
/// <remarks>
/// <see cref="Bands"/> is what makes a picture answerable to the same question a
/// gradient is — what is behind this line of text — which is the whole of how
/// <see cref="ModuleSkin.ContrastText"/> works over one.
/// </remarks>
internal sealed class ModuleArtwork
{
    private ModuleArtwork(IReadOnlyList<IImage> frames, IReadOnlyList<double> until, Color[] bands)
    {
        Frames = frames;
        Until = until;
        Bands = bands;
        Mean = Average(bands);
    }

    public IReadOnlyList<IImage> Frames { get; }

    /// <summary>When each frame gives way, in milliseconds from the first.</summary>
    public IReadOnlyList<double> Until { get; }

    /// <summary>How long the whole thing runs, and nought where it does not move.</summary>
    public double Runs => Until.Count == 0 ? 0 : Until[^1];

    /// <summary>The picture's color in each of <see cref="Bands"/> bands, top to bottom.</summary>
    public Color[] Bands { get; }

    public Color Mean { get; }

    /// <summary>Which frame is showing at <paramref name="millis"/> into the run.</summary>
    public IImage At(double millis)
    {
        if (Frames.Count == 1 || Runs <= 0) return Frames[0];

        var into = millis % Runs;

        for (var frame = 0; frame < Until.Count; frame++)
            if (into < Until[frame])
                return Frames[frame];

        return Frames[^1];
    }

    /// <summary>The picture's color across the band at <paramref name="fraction"/> down it.</summary>
    public Color Band(double fraction) =>
        Bands[Math.Clamp((int)(fraction * Bands.Length), 0, Bands.Length - 1)];

    // --- decoding -----------------------------------------------------------

    /// <summary>
    /// The picture this skin carries, read once and kept — the failure too, so
    /// bytes that are not a picture are not decoded again every frame. Null is
    /// what a module falls back to its category on.
    /// </summary>
    public static ModuleArtwork? Of(ModuleSkin.Artwork skin)
    {
        if (read.TryGetValue(skin, out var kept)) return kept;

        ModuleArtwork? made;

        try
        {
            made = IsVector(skin.Bytes.Span) ? Vector(skin) : Raster(skin);
        }
        catch (Exception)
        {
            made = null;
        }

        read[skin] = made;

        return made;
    }

    private static readonly Dictionary<ModuleSkin.Artwork, ModuleArtwork?> read = [];

    /// <summary>
    /// Whether the bytes are SVG, which is a question about text where every
    /// other format this reads answers with a magic number. An XML declaration,
    /// a doctype or a comment may come before the root element, so what is
    /// looked for is the tag rather than the first byte.
    /// </summary>
    private static bool IsVector(ReadOnlySpan<byte> bytes)
    {
        var head = bytes[..Math.Min(bytes.Length, Sniff)];

        return System.Text.Encoding.UTF8.GetString(head).Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How far into the file the sniff looks for an SVG root.</summary>
    private const int Sniff = 1024;

    private static ModuleArtwork? Vector(ModuleSkin.Artwork skin)
    {
        var source = SvgSource.LoadFromStream(new MemoryStream(skin.Bytes.ToArray()));

        if (source is null) return null;

        var image = new SvgImage { Source = source };

        if (image.Size.Width <= 0 || image.Size.Height <= 0) return null;

        using var read = Rasterized(image);

        return new ModuleArtwork([image], [], Sampled(read));
    }

    private static ModuleArtwork? Raster(ModuleSkin.Artwork skin)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(skin.Bytes.ToArray()));

        if (codec is null) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        if (info.Width <= 0 || info.Height <= 0) return null;

        var timings = codec.FrameInfo;
        var count = skin.Animate ? Math.Min(timings.Length, Longest) : 1;

        // A still is one frame however the codec counts them, and a GIF of one
        // frame is a still: neither gets a clock.
        if (count <= 1) return Still(codec, info);

        var frames = new List<IImage>(count);
        var until = new List<double>(count);
        var running = 0d;

        // The frame the bands are read from is the one a still module shows, so
        // text over an animation is colored against what it rests on.
        SKBitmap? opening = null;

        // The canvas every frame is composed onto. A GIF frame may be a patch
        // over the one before it, so the pixels are accumulated here and copied
        // out, rather than each frame being decoded on its own.
        using var canvas = new SKBitmap(info);

        for (var frame = 0; frame < count; frame++)
        {
            var options = timings[frame].RequiredFrame == frame - 1
                ? new SKCodecOptions(frame, frame - 1)
                : new SKCodecOptions(frame);

            if (codec.GetPixels(info, canvas.GetPixels(), options) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                break;

            using var snapshot = canvas.Copy();

            var fitted = Fitted(snapshot);

            frames.Add(Avalonian(fitted));

            if (opening is null) opening = fitted; else fitted.Dispose();

            running += timings[frame].Duration > 0 ? timings[frame].Duration : Rest;
            until.Add(running);
        }

        using (opening)
        {
            if (opening is null || frames.Count == 0) return null;

            return new ModuleArtwork(frames, frames.Count == 1 ? [] : until, Sampled(opening));
        }
    }

    private static ModuleArtwork? Still(SKCodec codec, SKImageInfo info)
    {
        using var bitmap = new SKBitmap(info);

        if (codec.GetPixels(info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        using var fitted = Fitted(bitmap);

        return new ModuleArtwork([Avalonian(fitted)], [], Sampled(fitted));
    }

    /// <summary>
    /// What a frame with no stated delay waits, which is what a browser does with
    /// the same file.
    /// </summary>
    private const double Rest = 100;

    /// <summary>
    /// How many frames of a moving picture are kept. A module is a couple of
    /// hundred pixels of canvas and every frame is held decoded, so a long
    /// animation is cut rather than carried.
    /// </summary>
    private const int Longest = 120;

    /// <summary>
    /// The longest side a decoded frame is held at, for the same reason: a module
    /// is never drawn near this large, and the file may be.
    /// </summary>
    private const int Biggest = 512;

    /// <summary>The same picture, brought down to <see cref="Biggest"/> if it is over it.</summary>
    private static SKBitmap Fitted(SKBitmap bitmap)
    {
        var side = Math.Max(bitmap.Width, bitmap.Height);

        if (side <= Biggest) return bitmap.Copy();

        var scale = Biggest / (double)side;

        return bitmap.Resize(
            new SKImageInfo((int)(bitmap.Width * scale), (int)(bitmap.Height * scale), bitmap.ColorType, bitmap.AlphaType),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>An SVG drawn small, so its bands can be read the way a raster's are.</summary>
    private static SKBitmap Rasterized(SvgImage image)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Read, Read, SKColorType.Bgra8888, SKAlphaType.Premul));

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);

        if (image.Source?.Picture is { } picture)
        {
            var bounds = picture.CullRect;

            if (bounds.Width > 0 && bounds.Height > 0)
            {
                canvas.Scale(Read / bounds.Width, Read / bounds.Height);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(picture);
            }
        }

        return bitmap;
    }

    /// <summary>The side a picture is read at when all that is wanted is its bands.</summary>
    private const int Read = 64;

    /// <summary>
    /// The picture's color in each of <see cref="Depth"/> horizontal bands, which
    /// is what text over it is colored against.
    /// </summary>
    /// <remarks>
    /// Averaged over a band rather than sampled at a point, because a line of
    /// text covers a band and what is wanted is whether it can be read across the
    /// whole of one. Transparent pixels count as the canvas showing through, so
    /// artwork with a hole in it is not read as whatever happens to be painted in
    /// the hole.
    /// </remarks>
    private static Color[] Sampled(SKBitmap bitmap)
    {
        var bands = new Color[Depth];
        var ground = Colors.Node;

        for (var band = 0; band < Depth; band++)
        {
            var from = bitmap.Height * band / Depth;
            var to = Math.Max(from + 1, bitmap.Height * (band + 1) / Depth);

            double red = 0, green = 0, blue = 0;
            var counted = 0;

            // Every few pixels across and down: a band of a 512-wide frame is
            // thousands of reads for a number that moves by nothing.
            for (var y = from; y < to && y < bitmap.Height; y += Every)
            for (var x = 0; x < bitmap.Width; x += Every)
            {
                var pixel = bitmap.GetPixel(x, y);
                var over = pixel.Alpha / 255d;

                red += pixel.Red * over + ground.R * (1 - over);
                green += pixel.Green * over + ground.G * (1 - over);
                blue += pixel.Blue * over + ground.B * (1 - over);
                counted++;
            }

            bands[band] = counted == 0
                ? ground
                : Color.FromRgb((byte)(red / counted), (byte)(green / counted), (byte)(blue / counted));
        }

        return bands;
    }

    /// <summary>How many bands a picture is read in, down its height.</summary>
    private const int Depth = 32;

    /// <summary>One pixel in this many, each way, is read.</summary>
    private const int Every = 3;

    private static Color Average(Color[] bands)
    {
        double red = 0, green = 0, blue = 0;

        foreach (var band in bands)
        {
            red += band.R;
            green += band.G;
            blue += band.B;
        }

        return Color.FromRgb(
            (byte)(red / bands.Length),
            (byte)(green / bands.Length),
            (byte)(blue / bands.Length));
    }

    /// <summary>
    /// Skia's pixels as something the canvas can draw. Copied rather than
    /// wrapped, so the bitmap it was read from is the caller's to dispose.
    /// </summary>
    private static Bitmap Avalonian(SKBitmap bitmap) => new(
        PixelFormat.Bgra8888,
        AlphaFormat.Premul,
        bitmap.GetPixels(),
        new PixelSize(bitmap.Width, bitmap.Height),
        new Avalonia.Vector(96, 96),
        bitmap.RowBytes);
}
