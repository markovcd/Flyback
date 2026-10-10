using Flyback.Core.Graph;

namespace Flyback.Editor.Keys;

/// <summary>One key on the screen: the computer key it stands for and the note it plays.</summary>
/// <param name="Code">The browser's name for the computer key: <c>KeyZ</c>, <c>Comma</c>.</param>
/// <param name="Note">The MIDI note it plays.</param>
/// <param name="Sharp">Whether it is drawn as a black key, short and astride the gap between two white ones.</param>
/// <param name="Letter">What is printed on the computer key: <c>Z</c>, <c>,</c>.</param>
internal sealed record ScreenKey(string Code, int Note, bool Sharp, string Letter)
{
    /// <summary>The note's name, as the key is labeled: <c>C3</c>.</summary>
    public string Name => Pitch.Name(Note);
}
