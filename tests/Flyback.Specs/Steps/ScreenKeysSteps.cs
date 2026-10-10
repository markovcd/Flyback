using Avalonia;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The keys along the foot of the editor's window, and what a finger on them plays.</summary>
[Binding]
public sealed class ScreenKeysSteps(PatchContext context, EditorDriver editor)
{
    [Given("the patch lays the computer keyboard out in {word} {word}")]
    public void GivenTheKeyboardIsLaidOut(string tonic, string scale)
    {
        var mode = Chords.Scales.Single(s => string.Equals(s.Name, scale, StringComparison.OrdinalIgnoreCase));

        context.Patch.Keyboard = new KeyboardScale(Enumerable.Range(0, Pitch.Classes).Single(c => Pitch.ClassName(c) == tonic), mode.Id);
    }

    [When("a finger taps the canvas")]
    public void WhenAFingerTapsTheCanvas()
    {
        var screen = context.Node("screen");

        editor.TapFinger(new Point(screen.X, screen.Y + 250));
    }

    [When("the keys button is pressed in")]
    public void WhenTheKeysButtonIsPressedIn() => editor.Toggle("keys", true);

    [When("a finger presses the {string} key on the screen")]
    public void WhenAFingerPressesAKey(string note) => editor.PressKey(note);

    [When("the finger lifts")]
    public void WhenTheFingerLifts() => editor.LiftKey();

    [When("the octave up button is pressed")]
    public void WhenOctaveUp() => editor.PressPanelButton("octave-up");

    [Then("the keys on the screen are up")]
    public void ThenTheKeysAreUp() => editor.KeysUp.ShouldBeTrue();

    [Then("the keys on the screen run from {word}")]
    public void ThenTheKeysRunFrom(string note) => editor.Keys[0].ShouldBe(note);

    [Then("the keys on the screen play {}")]
    public void ThenTheKeysAre(string notes) =>
        editor.Keys.ShouldBe(notes.Replace(" and ", ", ", StringComparison.Ordinal).Split(", "));

    [Then("the MIDI In plays {word}")]
    public void ThenTheMidiInPlays(string note)
    {
        editor.KeyboardHeld(MidiSignal.Gate).ShouldBe(1d);
        Pitch.Name((float)editor.KeyboardHeld(MidiSignal.Pitch)).ShouldBe(note);
    }

    [Then("the MIDI In is let go")]
    public void ThenTheMidiInIsLetGo() => editor.KeyboardHeld(MidiSignal.Gate).ShouldBe(0d);

    [Then("the toolbar has no keys button")]
    public void ThenNoKeysButton() => editor.ToolbarShows("keys").ShouldBeFalse();
}
