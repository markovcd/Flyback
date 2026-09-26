namespace Flyback.Core.Language;

/// <summary>One stretch of a source file, and what should stand there instead.</summary>
/// <remarks>
/// A span rather than a whole new file, so that applying it leaves the caret
/// where it was and is one thing to take back.
/// </remarks>
public readonly record struct Change(int Offset, int Length, string Text);