using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Flyback.Editor.Canvas;

/// <summary>A measured color's picture, as a bitmap at the measuring grid's size.</summary>
internal static class MeasureFrames
{
    /// <param name="rgb">r, g and b a pixel, row by row from the top, as a measurement keeps them.</param>
    /// <param name="columns"></param>
    /// <param name="rows"></param>
    public static WriteableBitmap Bitmap(float[] rgb, int columns, int rows)
    {
        var bitmap = new WriteableBitmap(new PixelSize(columns, rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        var pixels = new byte[columns * rows * 4];

        for (var p = 0; p < columns * rows; p++)
        {
            pixels[p * 4 + 0] = Byte(rgb[p * 3 + 2]);
            pixels[p * 4 + 1] = Byte(rgb[p * 3 + 1]);
            pixels[p * 4 + 2] = Byte(rgb[p * 3 + 0]);
            pixels[p * 4 + 3] = 255;
        }

        using var locked = bitmap.Lock();

        var stride = columns * 4;

        for (var y = 0; y < rows; y++)
            Marshal.Copy(pixels, y * stride, locked.Address + y * locked.RowBytes, stride);

        return bitmap;
    }

    /// <summary>What the screen would show of a value: clamped to 0..1, as a frame is.</summary>
    private static byte Byte(float v) => (byte)Math.Round(Math.Clamp(float.IsFinite(v) ? v : 0f, 0f, 1f) * 255f);
}
