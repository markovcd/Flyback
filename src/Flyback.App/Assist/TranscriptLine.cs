namespace Flyback.App.Assist;

/// <summary>One line of the transcript, as the panel showed it.</summary>
public sealed record TranscriptLine(Voice Voice, string Text);