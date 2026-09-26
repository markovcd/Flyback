namespace Flyback.Core.Language;

/// <summary>
/// Placing a module, or calling a <c>def</c>.
/// </summary>
/// <param name="Target">
/// A short name, a type id written in full, or the name of a def. Which it is
/// cannot be known until the catalog and the defs are both in hand, so the
/// parser records what was written and leaves it.
/// </param>
/// <param name="Block">
/// What the module carries that is not a knob, still as the text it was written
/// as — see <see cref="StepNotation"/>.
/// </param>
/// <param name="BlockLine">Where the block's '[' is, for a complaint about what is inside it.</param>
/// <param name="BlockColumn">Where the block's '[' is, for a complaint about what is inside it.</param>
public sealed record CallExpr(
    string Target,
    IReadOnlyList<Argument> Arguments,
    string? Block,
    int Line,
    int Column,
    int BlockLine = 0,
    int BlockColumn = 0) : Expr(Line, Column);