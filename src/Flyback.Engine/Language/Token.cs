namespace Flyback.Core.Language;

/// <summary>
/// One token, and where it came from so that a complaint can point at it.
/// </summary>
/// <param name="Value">
/// What a <see cref="TokenKind.Number"/> is worth. The scale a socket reads a
/// note or a duration on is decided in the lexer, because that is where the
/// spelling still exists.
/// </param>
/// <param name="Scaled">
/// Whether this number was written as a note or a duration rather than a bare
/// figure. The binder checks it against the port's
/// <see cref="Graph.PortDisplay"/>, so <c>20ms</c> on a plain socket is a
/// complaint rather than a silent -1.699.
/// </param>
public readonly record struct Token(
    TokenKind Kind,
    string Text,
    int Line,
    int Column,
    double Value = 0d,
    NumberStyle Scaled = NumberStyle.Plain);