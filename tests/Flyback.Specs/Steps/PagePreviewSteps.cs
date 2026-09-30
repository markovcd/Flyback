using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The preview host with a page's WebGL surface or the desktop's, and when it hands the picture to the processor.</summary>
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

    [Then("the preview stays on the GPU")]
    public void ThenOnTheGpu() => Headless.Run(() =>
    {
        host!.Backend.ShouldBe(PreviewBackend.Gpu);
        host.Child.ShouldBeSameAs(surface);
    });

    private void Build(bool processorStandsIn) => Headless.Run(() =>
    {
        surface = new StandInSurface(processorStandsIn);
        host = new PreviewHost(() => surface);
    });

    /// <summary>A picture that is a Scope's chart of a sine, a table the shader reads as a texture.</summary>
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
        program.Tables.ShouldNotBeEmpty("the chart is a table, and a picture without one proves nothing");

        return program;
    }
}
