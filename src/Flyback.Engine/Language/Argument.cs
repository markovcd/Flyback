namespace Flyback.Core.Language;

/// <summary>One argument to a call, named or not.</summary>
/// <param name="Name">The socket this is for, or null to take the next free one.</param>
public sealed record Argument(string? Name, Expr Value, int Line, int Column);