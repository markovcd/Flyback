using System.Numerics;
using Flyback.Core.Compile;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// An SVG, an OBJ or a PNG, read into one closed path for a Path to play.
/// </summary>
/// <remarks>
/// Picked by the file's extension. Never throws: a file is hostile until read, so the
/// bytes, the points and the strokes are capped and a file past them is refused by
/// name.
/// </remarks>
internal static class ShapeReader
{
    /// <summary>The largest file read, in bytes.</summary>
    internal const int MostBytes = 16 * 1024 * 1024;

    /// <summary>The most points read from a file, before it is spaced evenly.</summary>
    internal const int MostPoints = 1_000_000;

    /// <summary>Whether a name is a picture's, read from what the host decoded rather than from its bytes.</summary>
    internal static bool IsPicture(string name) =>
        string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase);

    /// <param name="image">A picture the host decoded.</param>
    /// <param name="fault">Why nothing came back, or <see cref="ShapeFault.None"/>.</param>
    internal static LoadedShape? Read(LoadedImage image, out ShapeFault fault)
    {
        var strokes = PngStrokes.Read(image, ShapeLayout.MostStrokes, out fault);

        return strokes is null ? null : ShapeLayout.Of(strokes, flat: true, out fault);
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

            default:
                fault = ShapeFault.Unsupported;
                return null;
        }

        return strokes is null ? null : ShapeLayout.Of(strokes, flat, out fault);
    }

    /// <summary>What a fault means, said to the person who chose the file.</summary>
    internal static string Explain(ShapeFault fault) => fault switch
    {
        ShapeFault.Unsupported => "a Path reads an .svg, an .obj or a .png.",
        ShapeFault.NotShape => "it is not the kind of file its name says.",
        ShapeFault.Empty => "nothing in it draws.",
        ShapeFault.TooBig => $"it is too big: more than {MostBytes / 1024 / 1024} MB, "
            + $"{MostPoints:N0} points or {ShapeLayout.MostStrokes:N0} strokes.",
        _ => "it could not be read.",
    };
}
