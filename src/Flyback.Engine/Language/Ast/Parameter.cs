namespace Flyback.Engine.Language.Ast;

/// <summary>One parameter of a <c>def</c>, with what a call that leaves it out gets.</summary>
/// <param name="Default">A number, a note, a duration or a text, or null where every call must say.</param>
public sealed record Parameter(string Name, Expr? Default, int Line, int Column)
{
    /// <summary>
    /// What follows <c>in</c> after the default: a range (<c>0..100</c>) a module's socket turns
    /// through, or a list (<c>[16, 32, 64]</c>) of the choices a module's setting offers; null for neither.
    /// </summary>
    public Expr? Within { get; init; }
}
