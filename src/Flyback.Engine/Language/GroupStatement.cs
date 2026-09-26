namespace Flyback.Core.Language;

// --- expressions ------------------------------------------------------------

// --- statements -------------------------------------------------------------

/// <summary><c>group "Name" { ... }</c>, a box drawn round what is declared inside it.</summary>
/// <param name="Name">Null for a group with no name; blocks with the same name are one group.</param>
public sealed record GroupStatement(
    string? Name,
    IReadOnlyList<Statement> Body,
    int Line,
    int Column) : Statement(Line, Column);
