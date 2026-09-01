namespace Flyback.Core.Language;

/// <summary>
/// Something wrong with a source file, said where it is.
/// </summary>
/// <remarks>
/// A value rather than an exception, for the reason every other boundary in the
/// engine reports that way — see <see cref="Graph.PatchLoad"/>. A file with four
/// mistakes in it should say all four, and a parser that throws can only ever
/// say the first.
/// </remarks>
/// <param name="Line">Counting from one, as an editor does.</param>
/// <param name="Column">Counting from one, as an editor does.</param>
public sealed record LanguageIssue(int Line, int Column, string Message)
{
    public override string ToString() => $"{Line}:{Column}: {Message}";
}

// --- expressions ------------------------------------------------------------

/// <summary>Anything that stands for a signal or a number.</summary>
public abstract record Expr(int Line, int Column);

/// <summary>A number as it was written, and how it was written.</summary>
public sealed record NumberExpr(double Value, NumberStyle Style, int Line, int Column)
    : Expr(Line, Column);

/// <summary>The one string the language has, which names a file.</summary>
public sealed record TextExpr(string Value, int Line, int Column) : Expr(Line, Column);

/// <summary>
/// A low and a high written as one thing, which fills two sockets rather than
/// one — <c>remap(-2..2, 0..1)</c> is four arguments spelled as two.
/// </summary>
public sealed record RangeExpr(Expr Low, Expr High, int Line, int Column) : Expr(Line, Column);

/// <summary>
/// A name, and optionally one of its outputs: <c>riff</c>, <c>riff.gate</c>,
/// <c>out.color</c>.
/// </summary>
public sealed record NameExpr(string Name, string? Port, int Line, int Column) : Expr(Line, Column);

/// <summary>One argument to a call, named or not.</summary>
/// <param name="Name">The socket this is for, or null to take the next free one.</param>
public sealed record Argument(string? Name, Expr Value, int Line, int Column);

/// <summary>
/// Placing a module, or calling a <c>def</c>.
/// </summary>
/// <param name="Target">
/// A short name, a type id written in full, or the name of a def. Which it is
/// cannot be known until the catalogue and the defs are both in hand, so the
/// parser records what was written and leaves it.
/// </param>
/// <param name="Block">
/// What the module carries that is not a knob, still as the text it was written
/// as — see <see cref="StepNotation"/>.
/// </param>
public sealed record CallExpr(
    string Target,
    IReadOnlyList<Argument> Arguments,
    string? Block,
    int Line,
    int Column) : Expr(Line, Column);

/// <summary>Infix arithmetic, which is the five binary maths modules by another spelling.</summary>
public sealed record BinaryExpr(TokenKind Operator, Expr Left, Expr Right, int Line, int Column)
    : Expr(Line, Column);

/// <summary>A leading minus, which is Negate.</summary>
public sealed record NegateExpr(Expr Value, int Line, int Column) : Expr(Line, Column);

/// <summary>
/// A signal flowing into a module. The whole of the language's shape, and the
/// only place <see cref="Binder"/> applies the pipe rule.
/// </summary>
public sealed record PipeExpr(Expr Source, Expr Stage, int Line, int Column) : Expr(Line, Column);

// --- statements -------------------------------------------------------------

/// <summary>One line of a patch.</summary>
public abstract record Statement(int Line, int Column);

/// <summary>
/// <c>let name = pipeline</c>. The name reaches the finished patch as the
/// node's own label, so a patch built from text opens on the canvas already
/// named.
/// </summary>
public sealed record LetStatement(string Name, Expr Value, int Line, int Column)
    : Statement(Line, Column);

/// <summary><c>let (a, b, c) = call</c>, which takes a def's several results apart.</summary>
public sealed record LetTupleStatement(
    IReadOnlyList<string> Names,
    Expr Value,
    int Line,
    int Column) : Statement(Line, Column);

/// <summary>
/// <c>def name(a, b) = body</c>. Expanded at every call site and never compiled
/// as itself, so nothing about it survives into the patch.
/// </summary>
public sealed record DefStatement(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<Statement> Body,
    Expr? Result,
    IReadOnlyList<Expr>? Results,
    int Line,
    int Column) : Statement(Line, Column);

/// <summary>A pipeline standing on its own, which is the only statement with an effect.</summary>
public sealed record PipelineStatement(Expr Value, int Line, int Column) : Statement(Line, Column);

/// <summary><c>name.port = value</c>, which turns a knob.</summary>
public sealed record KnobStatement(NameExpr Target, Expr Value, int Line, int Column)
    : Statement(Line, Column);

/// <summary>
/// <c>name.port &lt;- pipeline</c>, which is how a cycle is closed: the one wire
/// that cannot be written as a pipeline because it runs backwards.
/// </summary>
public sealed record BackWireStatement(NameExpr Target, Expr Value, int Line, int Column)
    : Statement(Line, Column);

/// <summary><c>group "Name" { ... }</c>, a box drawn round what is declared inside it.</summary>
public sealed record GroupStatement(
    string Name,
    IReadOnlyList<Statement> Body,
    int Line,
    int Column) : Statement(Line, Column);
