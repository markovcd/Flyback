using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Linking an input to a panel knob from its well on the canvas.</summary>
[Binding]
public sealed class LinkingSteps(PatchContext context, EditorDriver editor)
{
    /// <summary>What the knob gives the socket once it is linked.</summary>
    private float held;

    private NodeInstance Sine => context.Node("sine");

    private static int Freq => NodeCatalog.BuiltIn.Require(NodeCatalog.SineTypeId).Inputs.ToList().FindIndex(port => port.Name == "freq");

    [Given("the sine's freq is linked to a new panel knob")]
    public void GivenLinked()
    {
        editor.ClickWell(Sine.Id, Freq);
        editor.Press(PhysicalKey.L, RawInputModifiers.Alt);
        editor.PickFromKnobList("New knob");
        editor.KnobOf(Sine.Id, Freq).ShouldNotBeNull();
        held = editor.KnobReading(Sine.Id, Freq).ShouldNotBeNull();
    }

    [When("Alt+L is pressed")]
    public void WhenAltL() => editor.Press(PhysicalKey.L, RawInputModifiers.Alt);

    [When("{string} is picked from the list of knobs")]
    public void WhenPicked(string line) => editor.PickFromKnobList(line);

    [Then("the list of knobs reads {string}")]
    public void ThenListReads(string line) => editor.KnobList.ShouldContain(line);

    [Then("the sine's freq follows {string}")]
    public void ThenFollows(string knob) => editor.KnobOf(Sine.Id, Freq).ShouldBe(knob);

    [Then("the sine's freq is where the knob had it")]
    public void ThenWhereKnobHadIt() => editor.InputOf(Sine.Id, Freq).ShouldBe(held);

    [Then("the sine's freq follows no knob")]
    public void ThenFollowsNone() => editor.KnobOf(Sine.Id, Freq).ShouldBeNull();
}
