namespace Flyback.Core.Graph;

/// <summary>One part of the Song: its name, and its level in each of its own sections.</summary>
/// <param name="Name">What the text and the canvas call it, a name the text can write after a dot.</param>
/// <param name="Levels">Its sections, as many as it loops over.</param>
public sealed record SongPart(string Name, IReadOnlyList<PartLevel> Levels);
