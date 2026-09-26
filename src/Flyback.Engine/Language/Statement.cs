namespace Flyback.Core.Language;

/// <summary>One line of a patch.</summary>
public abstract record Statement(int Line, int Column);