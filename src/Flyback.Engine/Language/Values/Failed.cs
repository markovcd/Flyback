namespace Flyback.Engine.Language.Values;

/// <summary>
/// A name whose statement was refused. Bound all the same, so every line that
/// reads it fails without a word: the text is refused whole already, and a
/// complaint per reader would bury the one mistake there is.
/// </summary>
internal sealed record Failed : Value;
