using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>The editor controls that must leave a recording's patch alone.</summary>
[Binding]
public sealed class RecordingSteps(Editor editor)
{
    private bool remainedOpen;

    [Given("a recording is in progress")]
    public void GivenARecordingIsInProgress() => editor.BeginRecording();

    [Then("the Open and preset controls are disabled")]
    public void ThenOpeningControlsAreDisabled() => editor.CanOpenPatch.ShouldBeFalse();

    [When("someone tries to close the editor")]
    public void WhenSomeoneTriesToCloseTheEditor() => remainedOpen = editor.TryClose();

    [Then("the editor remains open")]
    public void ThenTheEditorRemainsOpen() => remainedOpen.ShouldBeTrue();
}
