using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Gpu;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The preview host with a page's WebGL surface or the desktop's, and what it falls back to.</summary>
[Binding]
public sealed class PagePreviewSteps
{
    private StandInSurface? surface;
    private PreviewHost? host;

    [Given("a preview in a page")]
    public void GivenInAPage() => Build(processorStandsIn: false);

    [Given("a preview on the desktop")]
    public void GivenOnTheDesktop() => Build(processorStandsIn: true);

    [When("it is handed a picture charted by a Scope")]
    public void WhenCharted() => Headless.Run(() => host!.Program = Charted());

    [When("its WebGL fails")]
    public void WhenItFails() => Headless.Run(() => surface!.Fail("This browser has no WebGL 2."));

    [When("the processor is chosen to draw the picture")]
    public void WhenProcessorChosen() => Headless.Run(() => host!.Use(PreviewBackend.Cpu));

    [Then("the preview stays on WebGL")]
    public void ThenOnWebGl() => Headless.Run(() =>
    {
        host!.Backend.ShouldBe(PreviewBackend.Gpu);
        host.Child.ShouldBeSameAs(surface);
    });

    [Then("the preview draws on the processor")]
    public void ThenOnTheProcessor() => Headless.Run(() => host!.Backend.ShouldBe(PreviewBackend.Cpu));

    [Then("it says it cannot draw a Scope")]
    public void ThenItSaysWhy() => Headless.Run(() =>
        UndrawnPicture.Why(host!.Program).ShouldNotBeNull().ShouldContain("cannot draw a Scope"));

    private void Build(bool processorStandsIn) => Headless.Run(() =>
    {
        surface = new StandInSurface(processorStandsIn);
        host = new PreviewHost(() => surface);
    });

    /// <summary>A picture that is a Scope's chart of a sine, which a shader cannot read.</summary>
    private static CompiledPatch Charted()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = b.Add("osc.sine", 0, 0);
        var scope = b.Add(NodeCatalog.ScopeTypeId, 100, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(sine, 0, scope, 0);
        b.Wire(sine, 0, output, NodeCatalog.OutputLeftPort);
        b.Wire(scope, 0, output, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn, played: true).Program;
        program.ShaderCanDraw.ShouldBeFalse("a Scope's chart is a table the shader cannot read");

        return program;
    }
}
