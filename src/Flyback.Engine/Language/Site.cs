namespace Flyback.Core.Language;

/// <summary>Where something stands in a source file, counting from one as an editor does.</summary>
public readonly record struct Site(int Line, int Column);