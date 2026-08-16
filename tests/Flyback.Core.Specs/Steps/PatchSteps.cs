using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

/// <summary>
/// The whole vocabulary the feature files are written in. Kept as one binding
/// class because the steps are general — every scenario names modules, wires
/// them, compiles and then inspects either the program or a rendered pixel.
/// </summary>
[Binding]
public sealed class PatchSteps(PatchContext context)
{
    private const float Tolerance = 1.5f / 255f;

    [Given("a patch containing:")]
    public void GivenAPatchContaining(DataTable modules)
    {
        foreach (var row in modules.Rows)
            context.Add(row["name"], row["module"]);
    }

    [Given("a node named {string} of unknown type {string}")]
    public void GivenANodeOfUnknownType(string name, string typeId) => context.AddUnknown(name, typeId);

    [Given("{string} output {string} is wired to {string} input {string}")]
    public void GivenAWire(string source, string sourcePort, string target, string targetPort) =>
        context.Wire(source, sourcePort, target, targetPort);

    [Given("{string} output {int} is wired to {string} input {string}")]
    public void GivenAWireFromPortIndex(string source, int sourcePort, string target, string targetPort) =>
        context.Wire(source, sourcePort, target, targetPort);

    [Given("{string} input {string} is set to {float}")]
    public void GivenAnInputValue(string name, string port, float value) =>
        context.SetInput(name, port, value);

    /// <summary>Simulates a patch saved before a module gained an input (ADR-0020).</summary>
    [Given("{string} has only {int} stored input values")]
    public void GivenTruncatedInputValues(string name, int count) =>
        context.Node(name).InputValues = [.. context.Node(name).InputValues.Take(count)];

    [When("the patch is compiled")]
    public void WhenThePatchIsCompiled() => context.Compile();

    [When("the patch is compiled for {word}")]
    public void WhenThePatchIsCompiledFor(string sink) => context.CompileFor(sink);

    [Then("the audio is silent")]
    public void ThenTheAudioIsSilent() =>
        // Silence means exactly zero, not nearly zero — see AudioRendererTests.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        context.RenderAudio().ShouldAllBe(v => v == 0f);

    [Then("the audio is not silent")]
    public void ThenTheAudioIsNotSilent() =>
        context.RenderAudio().Any(v => Math.Abs(v) > 0.01f).ShouldBeTrue();

    [Then("both audio channels are identical")]
    public void ThenBothChannelsMatch()
    {
        var buffer = context.RenderAudio();

        for (var frame = 0; frame < buffer.Length / 2; frame++)
            buffer[frame * 2 + 1].ShouldBe(buffer[frame * 2]);
    }

    [Then("compilation reports no issues")]
    public void ThenNoIssues() =>
        context.Result.Issues.ShouldBeEmpty(
            string.Join(" | ", context.Result.Issues.Select(i => i.Message)));

    [Then("compilation reports an issue containing {string}")]
    public void ThenAnIssueContaining(string fragment) =>
        context.Result.Issues
            .Any(i => i.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue($"issues were: {string.Join(" | ", context.Result.Issues.Select(i => i.Message))}");

    [Then("the program contains no {string} ops")]
    public void ThenNoOpsOfKind(string opCode) => context.CountOps(Parse(opCode)).ShouldBe(0);

    [Then("the program contains exactly {int} {string} op(s)")]
    public void ThenExactlyNOps(int expected, string opCode) =>
        context.CountOps(Parse(opCode)).ShouldBe(expected);

    [Then("{string} input {string} still holds {float}")]
    public void ThenTheStoredValueIsUnchanged(string name, string port, float expected) =>
        context.StoredInput(name, port).ShouldBe(expected);

    [Then("the program contains at least one {string} op")]
    public void ThenAtLeastOneOp(string opCode) =>
        context.CountOps(Parse(opCode)).ShouldBeGreaterThan(0);

    [Then("the centre pixel is about {float}, {float}, {float}")]
    public void ThenTheCentrePixelIs(float r, float g, float b)
    {
        var (actualR, actualG, actualB) = context.RenderCentre(1);

        actualR.ShouldBe(r, Tolerance, "red");
        actualG.ShouldBe(g, Tolerance, "green");
        actualB.ShouldBe(b, Tolerance, "blue");
    }

    [Then("rendering {int} frame(s) gives a centre brightness of about {float}")]
    public void ThenBrightnessAfterFrames(int frames, float expected)
    {
        var (r, g, b) = context.RenderCentre(frames);

        r.ShouldBe(expected, Tolerance, $"red after {frames} frame(s)");
        g.ShouldBe(expected, Tolerance, $"green after {frames} frame(s)");
        b.ShouldBe(expected, Tolerance, $"blue after {frames} frame(s)");
    }

    [Then("rewinding after {int} frames and rendering {int} frame(s) gives a centre brightness of about {float}")]
    public void ThenBrightnessAfterRewind(int before, int after, float expected)
    {
        var (r, g, b) = context.RenderCentreAfterReset(before, after);

        r.ShouldBe(expected, Tolerance, "red after rewind");
        g.ShouldBe(expected, Tolerance, "green after rewind");
        b.ShouldBe(expected, Tolerance, "blue after rewind");
    }

    [Then("the rendered image is entirely black")]
    public void ThenTheImageIsBlack() => context.RenderedFrameIsBlack(1).ShouldBeTrue();

    [Then("the rendered image is not black")]
    public void ThenTheImageIsNotBlack() => context.RenderedFrameIsBlack(1).ShouldBeFalse();

    /// <summary>
    /// Asks about the stored byte rather than about a fraction, which is the
    /// only way to say that nothing was encoded on the way out (ADR-0014).
    /// </summary>
    [Then("the centre pixel is byte {int}")]
    public void ThenTheCentrePixelIsByte(int expected) =>
        context.Render().RedByteAt(PatchContext.Width / 2, PatchContext.Height / 2).ShouldBe((byte)expected);

    [Then("the frame gets brighter towards the top")]
    public void ThenTheFrameGetsBrighterUpwards()
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
    /// were normalised to -1..1 like y instead of being widened by the aspect
    /// ratio, the disc would come out as an ellipse as wide as the frame's
    /// shape and these two counts would differ by that ratio.
    /// </summary>
    [Then("a circle is as wide as it is tall at {int} by {int}")]
    public void ThenACircleIsRound(int width, int height)
    {
        var (across, down) = context.Render(1, width, height).DarkExtent();

        across.ShouldBeGreaterThan(0, "no disc was found on the middle row");
        down.ShouldBeGreaterThan(0, "no disc was found down the middle column");

        // A pixel either side: the middle row of an even-height frame is half a
        // pixel off centre, so it cuts the disc just below its widest point.
        Math.Abs(across - down)
            .ShouldBeLessThanOrEqualTo(1, $"the disc is {across} across and {down} down");
    }

    /// <summary>
    /// Renders the same patch at two densities and compares every pixel of the
    /// coarser one against the point in the finer one that samples the same
    /// coordinate.
    /// </summary>
    /// <remarks>
    /// The two grids only line up if the finer is an <em>odd</em> multiple of
    /// the coarser: pixel centres sit at <c>(i + 0.5) / size</c>, so pixel
    /// <c>i</c> of the coarse grid and pixel <c>k * i + (k - 1) / 2</c> of the
    /// fine one are the same point exactly when <c>k</c> is odd. That makes
    /// this an equality between two samplings of one function rather than an
    /// approximation, and it pins the half-pixel offset at the same time.
    /// </remarks>
    [Then("the frame at {int} by {int} matches the frame at {int} by {int}")]
    public void ThenTheFramesMatch(int fineWidth, int fineHeight, int coarseWidth, int coarseHeight)
    {
        var scale = fineWidth / coarseWidth;

        (fineWidth % coarseWidth).ShouldBe(0, "the finer frame must be a whole multiple of the coarser");
        (fineHeight / coarseHeight).ShouldBe(scale, "both axes must be scaled by the same factor");
        (scale % 2).ShouldBe(1, "the multiple must be odd for the two grids' pixel centres to coincide");

        var coarse = context.Render(1, coarseWidth, coarseHeight);
        var fine = context.Render(1, fineWidth, fineHeight);
        var offset = (scale - 1) / 2;

        for (var y = 0; y < coarseHeight; y++)
        for (var x = 0; x < coarseWidth; x++)
        {
            var here = coarse.At(x, y);
            var there = fine.At(x * scale + offset, y * scale + offset);
            var where = $"at ({x}, {y}) of {coarseWidth}x{coarseHeight}";

            // One byte of slack, for the two frames rounding a value that lands
            // on a byte boundary in opposite directions.
            here.R.ShouldBe(there.R, 1f / 255f, $"red {where}");
            here.G.ShouldBe(there.G, 1f / 255f, $"green {where}");
            here.B.ShouldBe(there.B, 1f / 255f, $"blue {where}");
        }
    }

    private static OpCode Parse(string name) =>
        Enum.TryParse<OpCode>(name, ignoreCase: true, out var code)
            ? code
            : throw new ArgumentException($"'{name}' is not an OpCode.");
}
