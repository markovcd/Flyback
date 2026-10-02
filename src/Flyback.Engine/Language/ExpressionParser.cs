using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast;
using Flyback.Engine.Language.Ast.Expressions;

namespace Flyback.Engine.Language;

/// <summary>Parses expressions while sharing the statement parser's token cursor.</summary>
internal sealed class ExpressionParser
{
    private readonly Parser parser;
    private readonly List<LanguageIssue> issues;
    private readonly Dictionary<Expr, int> heights = new(ReferenceEqualityComparer.Instance);
    private int depth;

    internal ExpressionParser(Parser parser, List<LanguageIssue> issues)
    {
        this.parser = parser;
        this.issues = issues;
    }

    private Token Current => parser.Current;

    private Token Ahead(int by = 1) => parser.Ahead(by);

    /// <summary>
    /// The loosest thing there is. Everything else binds tighter, which is what
    /// makes <c>t * 0.2 |&gt; sine()</c> read the way it looks.
    /// </summary>
    internal Expr? Pipeline()
    {
        if (depth >= Parser.MaxDepth)
        {
            parser.Complain(IssueCode.TooDeep, Deep);
            return null;
        }

        depth++;

        try
        {
            return Chain();
        }
        finally
        {
            depth--;
        }
    }

    /// <summary>Stages joined by pipes, the body of <see cref="Pipeline"/>.</summary>
    private Expr? Chain()
    {
        if (Sum() is not { } left) return null;

        var piped = false;

        while (Current.Kind == TokenKind.Pipe)
        {
            var line = Current.Line;
            var column = Current.Column;

            parser.Advance();

            if (Stage() is not { } stage) return null;
            if (Built(new PipeExpr(left, stage, line, column), left, stage) is not { } joined) return null;

            left = joined;
            piped = true;
        }

        // Arithmetic on what a pipe just produced is a thing people write and
        // this cannot read. The pipe is the loosest operator there is —
        // `t * 0.2 |> sine()` needs it to be — so `s |> fract * 2` would have to
        // mean piping into `fract * 2`, which is nothing.
        if (piped && Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent
            or TokenKind.Plus or TokenKind.Minus)
        {
            parser.Complain(IssueCode.ArithmeticAfterPipeline,
                $"'{Current.Text}' cannot follow a pipeline. Put the pipeline in brackets to do "
                + $"arithmetic on what it made: (a |> b) {Current.Text} 2.");

            return null;
        }

        return left;
    }

    /// <summary>What may sit after a pipe: a module to place, or a socket to land in.</summary>
    private Expr? Stage()
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            parser.Complain(IssueCode.Syntax, "expected a module or a socket after '|>'.");
            return null;
        }

        return Primary();
    }

    private Expr? Sum()
    {
        if (Product() is not { } left) return null;

        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Current.Kind;
            var line = Current.Line;
            var column = Current.Column;

            parser.Advance();

            if (Product() is not { } right) return null;
            if (Built(new BinaryExpr(op, left, right, line, column), left, right) is not { } joined) return null;

            left = joined;
        }

        return left;
    }

    private Expr? Product()
    {
        if (Ranged() is not { } left) return null;

        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var op = Current.Kind;
            var line = Current.Line;
            var column = Current.Column;

            parser.Advance();

            if (Ranged() is not { } right) return null;
            if (Built(new BinaryExpr(op, left, right, line, column), left, right) is not { } joined) return null;

            left = joined;
        }

        return left;
    }

    /// <summary>
    /// A value, or two of them written as a span.
    /// </summary>
    /// <remarks>
    /// Looser than the leading minus, and that is the whole point of its sitting
    /// here: <c>-2..2</c> is a range from minus two, not the negation of a range
    /// from two. The other way round parses, compiles and means something else,
    /// which two of the presets would have shown the hard way.
    /// </remarks>
    private Expr? Ranged()
    {
        if (Unary() is not { } low) return null;
        if (Current.Kind != TokenKind.Range) return low;

        var line = Current.Line;
        var column = Current.Column;

        parser.Advance();

        if (Unary() is not { } high) return null;

        return Built(new RangeExpr(low, high, line, column), low, high);
    }

    /// <summary>A value with the minus signs before it, counted rather than recursed into.</summary>
    private Expr? Unary()
    {
        var signs = new List<Token>();

        while (Current.Kind == TokenKind.Minus)
        {
            signs.Add(Current);
            parser.Advance();
        }

        if (Primary() is not { } value) return null;

        for (var i = signs.Count - 1; i >= 0; i--)
        {
            if (Built(new NegateExpr(value, signs[i].Line, signs[i].Column), value) is not { } negated) return null;

            value = negated;
        }

        return value;
    }

    private Expr? Primary()
    {
        var line = Current.Line;
        var column = Current.Column;

        switch (Current.Kind)
        {
            case TokenKind.Number:
            {
                var token = Current;
                parser.Advance();
                return new NumberExpr(token.Value, token.Scaled, line, column);
            }

            case TokenKind.Text:
            {
                var value = Current.Text;
                parser.Advance();
                return new TextExpr(value, line, column);
            }

            case TokenKind.OpenParen:
            {
                var open = Current;
                parser.Advance();

                if (Pipeline() is not { } inner) return null;

                return parser.Expect(TokenKind.CloseParen, Closing(")", "(", open)) ? Selected(inner) : null;
            }

            case TokenKind.Identifier:
                return NameOrCall(line, column);

            case TokenKind.Pipe:
                parser.Complain(IssueCode.Syntax, "'|>' has nothing before it to pipe. Write what it carries first: sine() |> out.left.");
                return null;

            case TokenKind.OpenBrace:
                parser.Complain(IssueCode.Syntax, "'{' opens nothing here: it follows 'group \"name\"' or a def's '='.");
                return null;

            default:
                if (!parser.Closer()) parser.Complain(IssueCode.Syntax, $"expected a value here, but found {parser.Found()}.");
                return null;
        }
    }

    /// <summary>
    /// A dotted name, which is either a module to place or a binding to read —
    /// and the parser does not decide which. <c>space.rotate(...)</c> and
    /// <c>riff.gate</c> are the same shape until the catalog is consulted.
    /// </summary>
    private Expr? NameOrCall(int line, int column)
    {
        var parts = new List<string> { Current.Text };
        parser.Advance();

        while (Current.Kind == TokenKind.Dot && Ahead().Kind == TokenKind.Identifier)
        {
            parts.Add(Ahead().Text);
            parser.Advance(2);
        }

        // Not while a call is still to come: 'x(...).out' is the selector's to read.
        if (Current.Kind == TokenKind.Dot && Ahead().Kind != TokenKind.OpenParen)
        {
            parser.Advance();
            return Refuse(IssueCode.Syntax, $"expected the name of a socket or an output after '{string.Join('.', parts)}.', but found {parser.Found()}.");
        }

        if (Current.Kind != TokenKind.OpenParen)
        {
            return parts.Count switch
            {
                1 => new NameExpr(parts[0], null, line, column),
                2 => new NameExpr(parts[0], parts[1], line, column),
                // Only a call can carry a full type id, since nothing reads an
                // output off one.
                _ => Refuse(IssueCode.Syntax, $"'{string.Join('.', parts)}' is not a name this can read."),
            };
        }

        var open = Current;
        parser.Advance();

        var arguments = new List<Argument>();

        if (!parser.Take(TokenKind.CloseParen))
        {
            do
            {
                if (Argument() is not { } argument) return null;
                arguments.Add(argument);
            }
            while (parser.Take(TokenKind.Comma) && Current.Kind != TokenKind.CloseParen);

            if (!parser.Take(TokenKind.CloseParen))
            {
                // Two values side by side: a bare socket name wanted its colon, anything else a comma.
                if (AtValue() && arguments[^1] is { Name: null, Value: NameExpr { Port: null } bare })
                    parser.Complain(IssueCode.Syntax, $"expected ':' after '{bare.Name}' to give it a value.");
                else if (AtValue())
                    parser.Complain(IssueCode.Syntax, "expected ',' between the arguments.");
                else
                    parser.Complain(IssueCode.Syntax, $"expected {Closing(")", "(", open)}, but found {parser.Found()}.");

                return null;
            }
        }

        Token? block = null;

        if (Current.Kind == TokenKind.Block)
        {
            block = Current;
            parser.Advance();
        }

        var call = new CallExpr(
            string.Join('.', parts), arguments, block?.Text, line, column, block?.Line ?? 0, block?.Column ?? 0);

        return Built(call, [.. arguments.Select(argument => argument.Value)]) is { } built ? Selected(built) : null;
    }

    /// <summary>
    /// The outputs taken off what was just read, if any are:
    /// <c>tempo(bpm: 104).beats</c>.
    /// </summary>
    /// <remarks>
    /// Only a call and a bracketed pipeline come here. A binding's selector is
    /// read as part of its name, because <c>riff.gate</c> and <c>midi.in</c> are
    /// the same shape until the brackets after one of them say which it was.
    /// </remarks>
    private Expr? Selected(Expr source)
    {
        while (parser.Take(TokenKind.Dot))
        {
            if (Current.Kind != TokenKind.Identifier)
                return Refuse(IssueCode.Syntax, "expected the name of an output after '.'.");

            if (Built(new SelectExpr(source, Current.Text, Current.Line, Current.Column), source) is not { } selected) return null;

            source = selected;
            parser.Advance();
        }

        return source;
    }

    private int Height(Expr expr) => heights.TryGetValue(expr, out var height) ? height : 1;

    /// <summary><paramref name="node"/>, standing on <paramref name="parts"/>, or null where that makes it too tall.</summary>
    private Expr? Built(Expr node, params Expr[] parts)
    {
        var height = 1 + parts.Select(Height).DefaultIfEmpty(0).Max();

        if (height > Parser.MaxDepth)
        {
            issues.Add(new LanguageIssue(node.Line, node.Column, IssueCode.TooDeep, Deep));
            return null;
        }

        heights[node] = height;
        return node;
    }

    private static string Deep => $"this is nested more than {Parser.MaxDepth} deep. Break it up with 'let'.";

    private Expr? Refuse(string code, string message)
    {
        parser.Complain(code, message);
        return null;
    }

    internal Argument? Argument()
    {
        var line = Current.Line;
        var column = Current.Column;
        string? name = null;

        if (Current.Kind == TokenKind.Identifier && Ahead().Kind == TokenKind.Colon)
        {
            name = Current.Text;
            parser.Advance(2);
        }

        return Pipeline() is { } value ? new Argument(name, value, line, column) : null;
    }

    private bool AtValue() => Current.Kind
        is TokenKind.Number or TokenKind.Text or TokenKind.Identifier or TokenKind.OpenParen or TokenKind.Minus;

    private static string Closing(string closer, string opener, Token open) =>
        $"'{closer}' to close the '{opener}' on line {open.Line}, column {open.Column}";
}
