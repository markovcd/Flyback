using System.Globalization;
using Avalonia.Input;
using Flyback.App.Midi;
using Flyback.Core.Graph;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The computer keyboard laid out by scale, and the note each key plays.</summary>
[Binding]
public sealed class ComputerKeyboardSteps
{
    private const string Letter = "([A-Z])";

    /// <summary>The keys of each row, left to right.</summary>
    private static readonly string[] Rows = ["QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM"];

    private readonly ComputerKeyboard keyboard = new();

    [Given(@"^the computer keyboard is laid out in ([A-G]#?) (.+)$")]
    public void GivenLaidOut(string tonic, string scale)
    {
        var mode = Chords.Scales.Single(s => string.Equals(s.Name, scale, StringComparison.OrdinalIgnoreCase));

        keyboard.Scale = new KeyboardScale(Enumerable.Range(0, Pitch.Classes).Single(c => Pitch.ClassName(c) == tonic), mode.Id);
    }

    [Then($@"^the keys {Letter} to {Letter} play (.+)$")]
    public void ThenTheKeysPlay(string first, string last, string notes)
    {
        var row = Rows.Single(r => r.Contains(first[0], StringComparison.Ordinal));
        var keys = row[row.IndexOf(first[0], StringComparison.Ordinal)..(row.IndexOf(last[0], StringComparison.Ordinal) + 1)];

        keys.Select(k => keyboard.Note(Key(k)) is { } note ? Pitch.Name(note) : "nothing")
            .ShouldBe(notes.Replace(" and ", ", ", StringComparison.Ordinal).Split(", "));
    }

    [Then($@"^the key {Letter} plays nothing$")]
    public void ThenTheKeyPlaysNothing(string key) => keyboard.Note(Key(key[0])).ShouldBeNull();

    private static Key Key(char letter) => Enum.Parse<Key>(letter.ToString(CultureInfo.InvariantCulture));
}
