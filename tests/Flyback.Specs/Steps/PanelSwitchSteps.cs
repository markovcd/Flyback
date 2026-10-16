using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Switches and buttons on the knob panel, pressed with the mouse.</summary>
[Binding]
public sealed class PanelSwitchSteps(EditorDriver editor)
{
    [When("the switch {string} is clicked")]
    public void WhenClicked(string name) => editor.PressPanelSwitch(name);

    [When("the button {string} is held down")]
    public void WhenHeld(string name) => editor.PressPanelSwitch(name, hold: true);

    [When("the button {string} is let go")]
    public void WhenLetGo(string name) => editor.LetGoOfPanelSwitch(name);

    [Then("{string} is on")]
    public void ThenOn(string name) => editor.KnobAt(name).ShouldBe(1f);

    [Then("{string} is off")]
    public void ThenOff(string name) => editor.KnobAt(name).ShouldBe(0f);
}
