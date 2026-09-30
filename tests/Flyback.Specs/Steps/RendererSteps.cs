using Flyback.App.Controls;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>What the status bar and the stats line call whatever draws the picture.</summary>
[Binding]
public sealed class RendererSteps
{
    private string? named;

    [Given("a graphics context that is {word} and whose renderer says {string}")]
    public void GivenContext(string profile, string renderer) =>
        named = GraphicsApi.Name(embedded: profile == "embedded", renderer);

    [Given("the picture is drawn on the processor")]
    public void GivenProcessor() => named = GraphicsApi.Processor;

    [Then("the picture is said to be drawn through {}")]
    public void ThenNamed(string name) => named.ShouldBe(name);
}
