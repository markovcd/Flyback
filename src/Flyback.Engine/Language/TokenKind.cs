namespace Flyback.Core.Language;

/// <summary>What one piece of source text is.</summary>
public enum TokenKind
{
    Identifier,

    /// <summary>A plain number, and what a note or a duration has already become.</summary>
    Number,

    /// <summary>The text between two quotes, with nothing done to what is inside.</summary>
    Text,

    /// <summary>
    /// The raw contents of a bracketed block, captured whole rather than
    /// tokenised — see <see cref="StepNotation"/> for why.
    /// </summary>
    Block,

    Pipe,
    BackWire,
    Range,
    Assign,
    Comma,
    Colon,
    Dot,
    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,

    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    /// <summary>The end of a statement, once the continuation rules have had their say.</summary>
    NewLine,

    End,
}