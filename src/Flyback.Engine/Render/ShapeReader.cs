using System.Numerics;
using Flyback.Core.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// An SVG, an OBJ or a PNG, read into one closed path for a Path to play.
/// </summary>
/// <remarks>
/// Picked by the file's extension. Never throws: a file is hostile until read, so the
/// bytes, the points and the strokes are capped and a file past them is refused by
/// name.
/// </remarks>
public static class ShapeReader
{
    /// <summary>The largest file read, in bytes.</summary>
    public const int MostBytes = 16 * 1024 * 1024;

    /// <summary>The most points read from a file, before it is spaced evenly.</summary>
    public const int MostPoints = 1_000_000;

    /// <param name="path">The file.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static LoadedShape? Read(string path, out ShapeFault fault)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                fault = ShapeFault.Missing;
                return null;
            }

            if (info.Length > MostBytes)
            {
                fault = ShapeFault.TooBig;
                return null;
            }

            return Read(File.ReadAllBytes(path), path, out fault);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            fault = ShapeFault.Missing;
            return null;
        }
    }

    /// <param name="bytes">The file's bytes.</param>
    /// <param name="name">The file's name, whose extension says what it is.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static LoadedShape? Read(byte[] bytes, string name, out ShapeFault fault)
    {
        if (bytes.Length > MostBytes)
        {
            fault = ShapeFault.TooBig;
            return null;
        }

        List<Vector3[]>? strokes;
        var flat = true;

        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".svg":
                strokes = SvgStrokes.Read(bytes, MostPoints, out fault);
                break;

            case ".obj":
                strokes = ObjStrokes.Read(bytes, MostPoints, out fault);
                flat = false;
                break;

            case ".png":
                if (PngReader.Read(new MemoryStream(bytes), out _) is not { } image)
                {
                    fault = ShapeFault.NotShape;
                    return null;
                }

                strokes = PngStrokes.Read(image, ShapeLayout.MostStrokes, out fault);
                break;

            default:
                fault = ShapeFault.Unsupported;
                return null;
        }

        return strokes is null ? null : ShapeLayout.Of(strokes, flat, out fault);
    }

    /// <summary>What a fault means, said to the person who chose the file.</summary>
    internal static string Explain(ShapeFault fault) => fault switch
    {
        ShapeFault.Missing => "there is no file there.",
        ShapeFault.Unsupported => "a Path reads an .svg, an .obj or a .png.",
        ShapeFault.NotShape => "it is not the kind of file its name says.",
        ShapeFault.Elsewhere => "it is on another machine. Copy it beside the patch.",
        ShapeFault.Empty => "nothing in it draws.",
        ShapeFault.TooBig => $"it is too big: more than {MostBytes / 1024 / 1024} MB, "
            + $"{MostPoints:N0} points or {ShapeLayout.MostStrokes:N0} strokes.",
        _ => "it could not be read.",
    };
}
