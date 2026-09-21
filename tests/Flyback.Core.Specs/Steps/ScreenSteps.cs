using System.Globalization;
using Reqnroll;
using Shouldly;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

/// <summary>What the screen shows, rendered from the patch as it stands.</summary>
[Binding]
public sealed class ScreenSteps(PatchContext context)
{
    /// <summary>A byte and a half either way, for rounding onto the 0..255 grid.</summary>
    private const float Tolerance = 1.5f / 255f;

    [Then("the screen shows {float}")]
    public void ThenAGrey(float level) => ShouldShow(context.Render().Centre, level, level, level, "center");

    [Then("the screen shows {float}, {float}, {float}")]
    public void ThenAColor(float r, float g, float b) => ShouldShow(context.Render().Centre, r, g, b, "center");

    [Then("the screen is black")]
    public void ThenBlack() => context.Render().IsBlack.ShouldBeTrue();

    [Then("the screen is not black")]
    public void ThenNotBlack() => context.Render().IsBlack.ShouldBeFalse();

    /// <summary>The stored byte rather than a fraction, the only way to say nothing was encoded on the way out.</summary>
    [Then("each channel is stored as {int}")]
    public void ThenStoredAs(int expected)
    {
        var frame = context.Render();
        var (x, y) = (PatchContext.Width / 2, PatchContext.Height / 2);

        frame.RedByteAt(x, y).ShouldBe((byte)expected);
        frame.At(x, y).G.ShouldBe(expected / 255f);
        frame.At(x, y).B.ShouldBe(expected / 255f);
    }

    [Then(@"^each frame builds on the last: (.+)$")]
    public void ThenEachFrameBuilds(string list)
    {
        var expected = list.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        for (var i = 0; i < expected.Length; i++)
            ShouldShow(context.Render(i + 1).Centre, expected[i], expected[i], expected[i], $"frame {i + 1}");
    }

    [Then("after {int} frames and a rewind the next frame is back at {float}")]
    public void ThenRewound(int frames, float level) =>
        ShouldShow(context.RenderAfterRewind(frames, 1).Centre, level, level, level, "after rewind");

    [Then("the picture gets brighter towards the top")]
    public void ThenBrighterUpwards()
    {
        var frame = context.Render();

        var bottom = frame.AtFraction(0.5f, 0.9f).R;
        var middle = frame.AtFraction(0.5f, 0.5f).R;
        var top = frame.AtFraction(0.5f, 0.1f).R;

        middle.ShouldBeGreaterThan(bottom, "the middle of the frame should outrank the bottom");
        top.ShouldBeGreaterThan(middle, "the top of the frame should outrank the middle");
    }

    /// <summary>
    /// Counts the disc across the middle row and down the middle column. If x
    /// were normalized to -1..1 like y instead of being widened by the aspect
    /// ratio, the disc would be an ellipse and the two counts would differ by it.
    /// </summary>
    [Then("the disc is round at {int} by {int}")]
    public void ThenRound(int width, int height)
    {
        var (across, down) = context.Render(1, width, height).DarkExtent();

        across.ShouldBeGreaterThan(0, "no disc was found on the middle row");
        down.ShouldBeGreaterThan(0, "no disc was found down the middle column");

        // A pixel either side: the middle row of an even-height frame is half a
        // pixel off center, so it cuts the disc just below its widest point.
        Math.Abs(across - down).ShouldBeLessThanOrEqualTo(1, $"the disc is {across} across and {down} down");
    }

    /// <summary>
    /// Compares every pixel of the coarser frame against the pixel of the finer one
    /// that samples the same point.
    /// </summary>
    /// <remarks>
    /// Centers sit at <c>(i + 0.5) / size</c>, so pixel <c>i</c> and pixel
    /// <c>k * i + (k - 1) / 2</c> coincide exactly when <c>k</c> is odd. That makes
    /// this an equality, and pins the half-pixel offset at the same time.
    /// </remarks>
    [Then("the picture at {int} by {int} matches the picture at {int} by {int}")]
    public void ThenTheSizesMatch(int fineWidth, int fineHeight, int coarseWidth, int coarseHeight)
    {
        var scale = fineWidth / coarseWidth;

        (fineWidth % coarseWidth).ShouldBe(0, "the finer frame must be a whole multiple of the coarser");
        (fineHeight / coarseHeight).ShouldBe(scale, "both axes must be scaled by the same factor");
        (scale % 2).ShouldBe(1, "the multiple must be odd for the two grids' pixel centers to coincide");

        var coarse = context.Render(1, coarseWidth, coarseHeight);
        var fine = context.Render(1, fineWidth, fineHeight);
        var offset = (scale - 1) / 2;

        for (var y = 0; y < coarseHeight; y++)
        for (var x = 0; x < coarseWidth; x++)
        {
            var here = coarse.At(x, y);
            var there = fine.At(x * scale + offset, y * scale + offset);
            var where = $"at ({x}, {y}) of {coarseWidth}x{coarseHeight}";

            // One byte of slack, for a value on a byte boundary rounding both ways.
            here.R.ShouldBe(there.R, 1f / 255f, $"red {where}");
            here.G.ShouldBe(there.G, 1f / 255f, $"green {where}");
            here.B.ShouldBe(there.B, 1f / 255f, $"blue {where}");
        }
    }

    private static void ShouldShow((float R, float G, float B) pixel, float r, float g, float b, string where)
    {
        pixel.R.ShouldBe(r, Tolerance, $"red, {where}");
        pixel.G.ShouldBe(g, Tolerance, $"green, {where}");
        pixel.B.ShouldBe(b, Tolerance, $"blue, {where}");
    }
}
