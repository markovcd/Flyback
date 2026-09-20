using System.Globalization;

namespace Flyback.Core.Render;

/// <summary>A frame's size as a person types it, bounded by what a frame may hold.</summary>
public static class FrameSize
{
    /// <summary>WIDTHxHEIGHT as a frame this can draw, or null for one it cannot.</summary>
    public static (int Width, int Height)? Of(string text)
    {
        var parts = text.Split('x', 'X');

        if (parts.Length != 2
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            return null;
        }

        // A bound rather than a buffer: every allocation behind a frame is the
        // pixel count times four, and 27000x27000 overflows the multiplication
        // long before it exhausts anything.
        return (long)width * height > SynthRenderer.MostPixels ? null : (width, height);
    }

    /// <summary>Why <paramref name="text"/> is not a frame, said the way a shell says it.</summary>
    public static string Refuse(string text)
    {
        var parts = text.Split('x', 'X');

        if (parts.Length == 2
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var height)
            && width > 0
            && height > 0)
        {
            return $"{text} is {(long)width * height:N0} pixels, and a frame may be "
                + $"{SynthRenderer.MostPixels:N0} — about 8192x8192.";
        }

        return $"'{text}' is not a size — write it as WIDTHxHEIGHT, such as 1920x1080.";
    }
}
