namespace Flyback.Engine.Language.Ast;

/// <summary>One line of a patch.</summary>
public abstract record Statement(int Line, int Column)
{
    /// <summary>
    /// What the statement is called, for naming what it places (ADR-0067). A
    /// <c>let</c> has a name and a terminated pipeline has a socket; a line
    /// with nothing to be called by is null, and takes a number, which is the
    /// one case where inserting a line above moves something below it.
    /// </summary>
    public abstract string? Naming { get; }

    /// <summary>The names this line binds, for the lines after it to read.</summary>
    public virtual IEnumerable<string> Binds => [];
}
