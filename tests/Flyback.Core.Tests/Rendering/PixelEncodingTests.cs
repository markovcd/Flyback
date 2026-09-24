using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// How a level becomes a byte on the screen: linearly, with no gamma, and clamped
/// at both ends, so the number on a module predicts the pixel.
/// </summary>
public class PixelEncodingTests
{
    [Theory]
    [InlineData(0.5f, 128)] // sRGB would put this at 188.
    [InlineData(4f, 255)]
    [InlineData(-1f, 0)]
    public void A_level_is_stored_as_its_byte_with_no_gamma(float level, int expected)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Wire(b.Add("value", (0, level)), 0, b.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputColorPort);

        const int width = 4, height = 4, stride = width * 4;
        var pixels = new byte[stride * height];

        new SynthRenderer().Render(b.Patch.CompileForVideo().Program, 0d, width, height, pixels, stride);

        // Blue, green and red: the buffer is BGRA.
        pixels[..3].ShouldBe([(byte)expected, (byte)expected, (byte)expected]);
    }
}
