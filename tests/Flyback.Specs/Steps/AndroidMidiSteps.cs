using Flyback.Plugins.Midi;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A keyboard heard as Android hands it over: one stream of bytes, read by the stream reader the Android plugin uses.</summary>
[Binding]
public sealed class AndroidMidiSteps
{
    private const byte NoteOn = 0x90;

    private const byte Clock = 0xF8;

    private const byte Force = 0x40;

    private readonly List<(MidiAction Action, int Note)> heard = [];

    private MidiStream? keyboard;

    private MidiStream Keyboard => keyboard.ShouldNotBeNull();

    [Given("a keyboard whose notes arrive as a stream of bytes")]
    public void GivenAKeyboardHeardAsAStream() => keyboard = new MidiStream(message => heard.Add((message.Action, message.Note)));

    [When("it sends {word} pressed, cut in two")]
    public void WhenItSendsANoteCutInTwo(string note)
    {
        Keyboard.Feed([NoteOn, (byte)KeyboardSteps.NoteNumber(note)]);
        Keyboard.Feed([Force]);
    }

    [When("it sends {} pressed under one status byte")]
    public void WhenItSendsNotesUnderOneStatusByte(string notes) =>
        Keyboard.Feed([NoteOn, .. KeyboardSteps.Parse(notes).SelectMany(note => new[] { (byte)note, Force })]);

    [When("it sends {word} pressed with a clock tick between its bytes")]
    public void WhenItSendsANoteAroundATick(string note) =>
        Keyboard.Feed([NoteOn, Clock, (byte)KeyboardSteps.NoteNumber(note), Force]);

    [Then("{word} is heard pressed once")]
    public void ThenTheNoteIsHeardOnce(string note) => heard.ShouldBe([Pressed(note)]);

    [Then("{} are heard pressed")]
    public void ThenTheNotesAreHeard(string notes) => heard.ShouldBe([.. KeyboardSteps.Split(notes).Select(Pressed)]);

    [Then("a tick and {word} pressed are heard")]
    public void ThenATickAndTheNoteAreHeard(string note) => heard.ShouldBe([(MidiAction.Tick, 0), Pressed(note)]);

    private static (MidiAction, int) Pressed(string note) => (MidiAction.Down, KeyboardSteps.NoteNumber(note));
}
