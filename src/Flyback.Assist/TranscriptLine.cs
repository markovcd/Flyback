namespace Flyback.Assist;

/// <summary>One line of the transcript, as the panel showed it.</summary>
internal sealed record TranscriptLine(Voice Voice, string Text);
