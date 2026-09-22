using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;

namespace Flyback.Core.Specs.Steps;

/// <summary>How a module's knob turns in the editor, read from the catalogue.</summary>
[Binding]
public sealed class KnobSteps
{
    private PortSpec knob;
    private ControlLink link;

    [Given("a {word} from the catalogue")]
    public void GivenAModule(string name)
    {
        var def = NodeCatalog.All.Single(d => d.Name == name);
        knob = def.Inputs.Single(p => p.Name == "freq");
    }

    [Given("the {word} knob of a {word} from the catalogue")]
    public void GivenAKnob(string socket, string name) =>
        knob = NodeCatalog.All.Single(d => d.Name == name).Inputs.Single(p => p.Name == socket);

    [Then("each half of its travel covers the same number of octaves")]
    public void ThenOctavesAreEven() =>
        At(0.5).ShouldBe(MathF.Sqrt(knob.Min * knob.Max), knob.Min * 1e-3f);

    [Then("its frequency knob turns from standing still to 20 kHz")]
    public void ThenStillToTheTopOfHearing() => Spans(0f, 20_000f);

    [Then("the lower half of its travel is slower than 20 Hz")]
    public void ThenLowerHalfIsSlow() => At(0.5).ShouldBeLessThanOrEqualTo(20f);

    [Then("the upper half of its travel is audible")]
    public void ThenUpperHalfIsAudible() => At(0.5).ShouldBeGreaterThanOrEqualTo(19.9f);

    [When("a panel knob is linked to its frequency")]
    public void WhenLinked() => link = ControlLink.For(Guid.NewGuid(), knob, knob.Default);

    [Given("a panel knob sweeping a socket from {float} to {float}")]
    public void GivenALinkedRange(float min, float max)
    {
        knob = new PortSpec("level", Min: 0f, Max: 1f);
        link = new ControlLink(Guid.NewGuid(), min, max);
    }

    [When("the knob is made logarithmic")]
    public void WhenLogarithmic() => link = link.Swept(true, knob);

    [Then("the panel knob halfway round sets it to {float}")]
    [Then("the panel knob halfway round sets it to {float} Hz")]
    public void ThenHalfwayReads(float value) => link.At(0.5f).ShouldBe(value, value * 1e-3f);

    [Then("made even again, halfway round sets it to {float}")]
    public void ThenEvenAgain(float value) => link.Swept(false, knob).At(0.5f).ShouldBe(value, value * 1e-3f);

    private void Spans(float bottom, float top)
    {
        knob.Min.ShouldBe(bottom);
        knob.Max.ShouldBe(top);
        At(0).ShouldBe(bottom);
        At(1).ShouldBe(top, 0.5f);
    }

    private float At(double travel) => knob.At(travel, knob.Min, knob.Max);
}
