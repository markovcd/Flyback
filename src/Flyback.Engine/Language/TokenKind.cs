namespace Flyback.Engine.Language;

/// <summary>What one piece of source text is.</summary>
public enum TokenKind
{
    Identifier,

    /// <summary>A plain number, and what a note or a duration has already become.</summary>
    Number,

    /// <summary><c>1.2.0</c>: three whole numbers joined by points, which only a pack's header line takes.</summary>
    Version,

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

    /// <summary><c>==</c>, and the comparisons after it, which only an <c>if</c>'s condition reads.</summary>
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,

    /// <summary><c>...</c>, which spreads a list into a call's sockets.</summary>
    Spread,

    /// <summary>The end of a statement, once the continuation rules have had their say.</summary>
    NewLine,

    End,
}
