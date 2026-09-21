using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;

namespace Flyback.Core.Specs.Steps;

/// <summary>How a module's knob turns in the editor, read from the catalogue.</summary>
[Binding]
public sealed class KnobSteps
{
    private PortSpec knob;

    [Given("a {word} from the catalogue")]
    public void GivenAModule(string name)
    {
        var def = NodeCatalog.All.Single(d => d.Name == name);
        knob = def.Inputs.Single(p => p.Name == "freq");
    }

    [Then("its frequency knob turns from standing still to 20 kHz")]
    public void ThenStillToTheTopOfHearing() => Spans(0f, 20_000f);

    [Then("the lower half of its travel is slower than 20 Hz")]
    public void ThenLowerHalfIsSlow() => At(0.5).ShouldBeLessThanOrEqualTo(20f);

    [Then("the upper half of its travel is audible")]
    public void ThenUpperHalfIsAudible() => At(0.5).ShouldBeGreaterThanOrEqualTo(19.9f);

    private void Spans(float bottom, float top)
    {
        knob.Min.ShouldBe(bottom);
        knob.Max.ShouldBe(top);
        At(0).ShouldBe(bottom);
        At(1).ShouldBe(top, 0.5f);
    }

    private float At(double travel) => knob.At(travel, knob.Min, knob.Max);
}
