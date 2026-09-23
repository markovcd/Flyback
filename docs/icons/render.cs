// Renders each file kind's SVG here into the .ico, .icns and .png Flyback ships.
//   dotnet run docs/icons/render.cs

#:package Svg.Skia@5.2.3
#:property ManagePackageVersionsCentrally=false
#:property RestorePackagesWithLockFile=false

using System.Buffers.Binary;
using System.Text.RegularExpressions;
using SkiaSharp;
using Svg.Skia;

var here = Path.GetDirectoryName((string)AppContext.GetData("EntryPointFilePath")!)!;
var output = Path.GetFullPath(Path.Combine(here, "..", "..", "src", "Flyback.App", "FileIcons"));
Directory.CreateDirectory(output);

// Windows picks the frame nearest the size it wants, scaling for DPI.
int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 256];

(string Type, int Size)[] icnsFrames =
[
    ("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
    ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512),
];

foreach (var kind in new[] { "patch", "bundle", "text", "plugin" })
{
    var svg = File.ReadAllText(Path.Combine(here, $"{kind}.svg"));

    File.WriteAllBytes(Path.Combine(output, $"{kind}.ico"), Ico(icoSizes.Select(size => (size, Png(svg, size))).ToArray()));
    File.WriteAllBytes(Path.Combine(output, $"{kind}.icns"), Icns(icnsFrames.Select(frame => (frame.Type, Png(svg, frame.Size))).ToArray()));
    File.WriteAllBytes(Path.Combine(output, $"{kind}.png"), Png(svg, 256));

    Console.WriteLine($"{kind}: {output}");
}

// The label is a smudge below 48 pixels, so the band carries the kind on its own.
static byte[] Png(string svg, int size)
{
    if (size < 48) svg = Regex.Replace(svg, @"<text id=""label""[^>]*>[^<]*</text>", "");

    using var document = new SKSvg();
    document.FromSvg(svg);

    var picture = document.Picture ?? throw new InvalidDataException("The SVG drew nothing.");
    var scale = size / picture.CullRect.Width;

    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.Scale(scale);
    surface.Canvas.DrawPicture(picture);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);

    return data.ToArray();
}

static byte[] Ico((int Size, byte[] Png)[] frames)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)frames.Length);

    var offset = 6 + 16 * frames.Length;

    foreach (var (size, png) in frames)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in frames) writer.Write(png);

    return stream.ToArray();
}

static byte[] Icns((string Type, byte[] Png)[] frames)
{
    using var stream = new MemoryStream();
    var length = new byte[4];

    stream.Write("icns"u8);
    BinaryPrimitives.WriteInt32BigEndian(length, 8 + frames.Sum(frame => 8 + frame.Png.Length));
    stream.Write(length);

    foreach (var (type, png) in frames)
    {
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        BinaryPrimitives.WriteInt32BigEndian(length, 8 + png.Length);
        stream.Write(length);
        stream.Write(png);
    }

    return stream.ToArray();
}
