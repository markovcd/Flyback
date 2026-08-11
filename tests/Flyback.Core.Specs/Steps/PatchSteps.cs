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

    private static OpCode Parse(string name) =>
        Enum.TryParse<OpCode>(name, ignoreCase: true, out var code)
            ? code
            : throw new ArgumentException($"'{name}' is not an OpCode.");
}
