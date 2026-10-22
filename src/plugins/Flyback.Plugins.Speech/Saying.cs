namespace Flyback.Plugins.Speech;

/// <summary>What a Speak was given to say, worked out: the words' tracks, and the tune when they are sung.</summary>
internal sealed record Saying(Utterance Said, Tune? Tune);
