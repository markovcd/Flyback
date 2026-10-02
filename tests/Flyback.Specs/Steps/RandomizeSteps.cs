using Avalonia.Input;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Randomizing the knob panel in the editor, through its shortcut and its buttons.</summary>
[Binding]
public sealed class RandomizeSteps(EditorDriver editor)
{
    [When("the knob panel is randomized")]
    public void WhenRandomized()
    {
        editor.RandomizeAtOnce();
        editor.PressCtrl(PhysicalKey.K, shift: true);
    }

    [When("the knobs are taken back")]
    public void WhenTakenBack() => editor.PressPanelButton("unroll-knobs");

    [Then("the knob {string} has moved from {float}")]
    public void ThenMoved(string name, float from) => editor.KnobAt(name).ShouldNotBe(from);

    [Then("the knob {string} still rests at {float}")]
    public void ThenStill(string name, float at) => editor.KnobAt(name).ShouldBe(at, 1e-6f);
}
