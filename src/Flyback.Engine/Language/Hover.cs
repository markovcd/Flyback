namespace Flyback.Engine.Language;

/// <summary>What one word of a patch's text is, for a tooltip over it.</summary>
/// <param name="Title">The word and what it is, on one line: <c>freq · input of Sine · 0 to 20000, at 1</c>.</param>
/// <param name="Body">What it is for, which may be empty.</param>
internal sealed record Hover(string Title, string Body);
