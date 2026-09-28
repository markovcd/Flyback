using Flyback.Core.Compile;
using Flyback.Core.Render;

namespace Flyback.Core.Tests;

/// <summary>
/// Compares two PNGs by their decoded pixels, exactly, and names the first pixel that differs.
/// </summary>
internal static class PngPixelComparer
{
    public static Task<CompareResult> Compare(Stream received, Stream verified, IReadOnlyDictionary<string, object> context) =>
        Task.FromResult(Compare(received, verified));

    public static CompareResult Compare(Stream received, Stream verified)
    {
        var got = PngReader.Read(received, out var receivedFault);
        if (got is null) return CompareResult.NotEqual($"The received PNG does not decode: {receivedFault}.");

        var want = PngReader.Read(verified, out var verifiedFault);
        if (want is null) return CompareResult.NotEqual($"The verified PNG does not decode: {verifiedFault}.");

        if (got.Width != want.Width || got.Height != want.Height)
            return CompareResult.NotEqual($"Received is {got.Width}x{got.Height}, verified is {want.Width}x{want.Height}.");

        var differing = 0;
        var first = -1;

        for (var pixel = 0; pixel < got.Width * got.Height; pixel++)
        {
            var i = pixel * 3;
            if (got.Pixels[i] == want.Pixels[i] && got.Pixels[i + 1] == want.Pixels[i + 1] && got.Pixels[i + 2] == want.Pixels[i + 2])
                continue;

            differing++;
            if (first < 0) first = pixel;
        }

        if (differing == 0) return CompareResult.Equal;

        var (x, y) = (first % got.Width, first / got.Width);

        return CompareResult.NotEqual(
            $"{differing} pixel(s) differ. The first is ({x}, {y}): received {Rgb(got, first)}, verified {Rgb(want, first)}.");
    }

    private static string Rgb(LoadedImage image, int pixel)
    {
        var i = pixel * 3;
        return $"rgb({Level(image.Pixels[i])}, {Level(image.Pixels[i + 1])}, {Level(image.Pixels[i + 2])})";
    }

    private static int Level(float value) => (int)MathF.Round(value * 255f);
}
