using Flyback.Core.Graph;

namespace Flyback.Engine.Language.Values;

/// <summary>
/// A panel knob, and the range a socket reads it over where the text gives
/// one: <c>cutoff</c>, or <c>cutoff(200..4000, knee: 20)</c>.
/// </summary>
/// <param name="Word">What the text calls it.</param>
internal sealed record Dial(PatchControl Control, string Word, Figure? Low = null, Figure? High = null, Figure? Knee = null) : Value;
