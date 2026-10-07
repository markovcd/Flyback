namespace Flyback.Engine.Language.Ast;

/// <summary>Anything that stands for a signal or a number.</summary>
public abstract record Expr(int Line, int Column)
{
    /// <summary>Whether the expression is written wholly in literals, and so places no module.</summary>
    public virtual bool Constant => false;

    /// <summary>Whether a pipeline is anywhere in the expression.</summary>
    public virtual bool Pipes => false;

    /// <summary>Whether the expression is <c>_</c>, which stands for what is piped in.</summary>
    public virtual bool Placeholder => false;

    /// <summary>Where the expression begins in the text, which an operator's own position is not.</summary>
    public virtual Expr Leftmost => this;
}