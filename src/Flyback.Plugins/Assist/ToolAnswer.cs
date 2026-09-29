namespace Flyback.Plugins.Assist;

/// <summary>One call answered: the words that go back to the model, and what it is shown or played with them.</summary>
/// <param name="Call">The call this answers.</param>
/// <param name="Text">The answer.</param>
/// <param name="Png">A picture that goes back with it, or null.</param>
/// <param name="Wav">A clip that goes back with it, or null; only ever set for a model that hears itself.</param>
public sealed record ToolAnswer(ToolCall Call, string Text, byte[]? Png = null, byte[]? Wav = null);
