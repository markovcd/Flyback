namespace Flyback.Engine.Language.Ast;

/// <summary>One line of a patch.</summary>
public abstract record Statement(int Line, int Column);