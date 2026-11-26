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

            case TokenKind.Version:
                parser.Complain(IssueCode.MalformedNumber,
                    $"'{Current.Text}' is not a number. Three whole numbers joined by points are a version, which only a pack's header takes: pack my.pack {Current.Text}.");
                parser.Advance();
                return null;

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

                if (Condition() is not { } inner) return null;

                if (Current.Kind == TokenKind.Comma) return Tupled(inner, open);

                return parser.Expect(TokenKind.CloseParen, Closing(")", "(", open)) ? Selected(inner) : null;
            }

            case TokenKind.Block:
                return Listed();

            case TokenKind.Identifier when AtLoop():
                return Loop(line, column);

            case TokenKind.Identifier when AtFold():
                return Fold(line, column);

            case TokenKind.Identifier when AtIf():
                return If(line, column);

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

    /// <summary>The rest of <c>(a, b, ...)</c> once its first item is read.</summary>
    private Expr? Tupled(Expr first, Token open)
    {
        var items = new List<Expr> { first };

        while (parser.Take(TokenKind.Comma) && Current.Kind != TokenKind.CloseParen)
        {
            if (Pipeline() is not { } item) return null;
            items.Add(item);
        }

        if (!parser.Expect(TokenKind.CloseParen, Closing(")", "(", open))) return null;

        return Built(new TupleExpr(items, open.Line, open.Column), [.. items]);
    }

    /// <summary>
    /// <c>[a, b, c]</c> where a value goes. The lexer keeps every bracket whole for the
    /// blocks a module carries, so the list's text is read again here as items.
    /// </summary>
    private Expr? Listed()
    {
        var block = Current;
        parser.Advance();

        var scanned = Lexer.Scan(block.Text, issues, block.Line, block.Column + 1);
        var inner = new Parser([.. scanned.Where(token => token.Kind != TokenKind.NewLine)], issues);

        if (inner.Items() is not { } items) return null;

        return Built(new ListExpr(items, block.Line, block.Column), [.. items]) is { } list ? Indexed(list, After(block)) : null;
    }

    /// <summary>Where a token ends on its line, for telling a bracket that touches it from one after a space.</summary>
    private static (int Line, int Column) After(Token token) => token.Kind == TokenKind.Block
        ? (token.Line, token.Column + token.Text.Length + 2)
        : (token.Line, token.Column + token.Text.Length);

    /// <summary>
    /// <c>xs[i]</c> and <c>xs[i][j]</c>: brackets that touch what is before them read an item of it.
    /// A bracket after a space is not an index, so a module's block is never mistaken for one.
    /// </summary>
    private Expr? Indexed(Expr source, (int Line, int Column) end)
    {
        while (Current.Kind == TokenKind.Block && (Current.Line, Current.Column) == end)
        {
            var block = Current;
            parser.Advance();

            var scanned = Lexer.Scan(block.Text, issues, block.Line, block.Column + 1);
            var inner = new Parser([.. scanned.Where(token => token.Kind != TokenKind.NewLine)], issues);

            if (inner.Items() is not { } items) return null;

            if (items.Count != 1)
                return Refuse(IssueCode.Syntax, "an item is read by one position: voices[2].");

            if (Built(new IndexExpr(source, items[0], block.Line, block.Column), source, items[0]) is not { } indexed) return null;

            source = indexed;
            end = After(block);
        }

        return source;
    }

    /// <summary>Whether a loop starts here: <c>for name in</c> or <c>for (a, b) in</c>.</summary>
    private bool AtLoop()
    {
        if (Current.Text != "for") return false;

        if (Ahead().Kind == TokenKind.Identifier) return Ahead(2) is { Kind: TokenKind.Identifier, Text: "in" };
        if (Ahead().Kind != TokenKind.OpenParen) return false;

        for (var by = 2; ; by += 2)
        {
            if (Ahead(by).Kind != TokenKind.Identifier) return false;
            if (Ahead(by + 1).Kind == TokenKind.CloseParen) return Ahead(by + 2) is { Kind: TokenKind.Identifier, Text: "in" };
            if (Ahead(by + 1).Kind != TokenKind.Comma) return false;
        }
    }

    /// <summary><c>for name in list { body }</c>, which <see cref="AtLoop"/> has already seen the start of.</summary>
    private Expr? Loop(int line, int column)
    {
        parser.Advance();

        var names = new List<string>();
        var tupled = parser.Take(TokenKind.OpenParen);

        do
        {
            names.Add(Current.Text);
            parser.Advance();
        }
        while (tupled && parser.Take(TokenKind.Comma));

        if (tupled) parser.Advance();

        parser.Advance();

        if (Pipeline() is not { } source) return null;

        if (!parser.Take(TokenKind.OpenBrace))
        {
            parser.Complain(IssueCode.Syntax, $"expected '{{' to open what each pass does, but found {parser.Found()}.");
            return null;
        }

        if (!parser.Braced(out var body, out var result, out var results)) return null;

        return Built(new ForExpr(names, tupled, source, body, result, results, line, column), source);
    }

    /// <summary>
    /// Whether a fold starts here: <c>fold</c> followed by what it goes over, or by the names
    /// each item takes. <c>fold</c> followed by anything else is a name, such as the Fold module's.
    /// </summary>
    private bool AtFold()
    {
        if (Current.Text != "fold") return false;

        if (Ahead().Kind is TokenKind.Number or TokenKind.Identifier or TokenKind.Block) return true;
        if (Ahead().Kind != TokenKind.OpenParen) return false;

        for (var by = 2; ; by += 2)
        {
            if (Ahead(by).Kind != TokenKind.Identifier) return false;
            if (Ahead(by + 1).Kind == TokenKind.CloseParen) return Ahead(by + 2) is { Kind: TokenKind.Identifier, Text: "in" };
            if (Ahead(by + 1).Kind != TokenKind.Comma) return false;
        }
    }

    /// <summary>
    /// <c>fold [names in] list from seed with names { body }</c>, which <see cref="AtFold"/> has
    /// already seen the start of: a loop whose passes each take what the one before ended with.
    /// </summary>
    private Expr? Fold(int line, int column)
    {
        parser.Advance();

        var names = new List<string>();
        var tupled = false;

        if (Current.Kind == TokenKind.OpenParen || (Current.Kind == TokenKind.Identifier && Ahead() is { Kind: TokenKind.Identifier, Text: "in" }))
        {
            tupled = Current.Kind == TokenKind.OpenParen;

            if (!Names(names, "each item")) return null;
            parser.Advance();
        }

        if (Pipeline() is not { } source) return null;

        if (Current is not { Kind: TokenKind.Identifier, Text: "from" })
        {
            parser.Complain(IssueCode.Syntax, $"expected 'from' and what the first pass starts with, but found {parser.Found()}.");
            return null;
        }

        parser.Advance();

        if (Pipeline() is not { } seed) return null;

        if (Current is not { Kind: TokenKind.Identifier, Text: "with" })
        {
            parser.Complain(IssueCode.Syntax, $"expected 'with' and the name each pass reads what the last ended with by, but found {parser.Found()}.");
            return null;
        }

        parser.Advance();

        var carried = new List<string>();
        var carriedTupled = Current.Kind == TokenKind.OpenParen;

        if (!Names(carried, "what is carried")) return null;

        if (!parser.Take(TokenKind.OpenBrace))
        {
            parser.Complain(IssueCode.Syntax, $"expected '{{' to open what each pass does, but found {parser.Found()}.");
            return null;
        }

        if (!parser.Braced(out var body, out var result, out var results)) return null;

        var carry = new Carry(carried, carriedTupled, seed);

        return Built(new ForExpr(names, tupled, source, body, result, results, line, column) { Carry = carry }, source, seed);
    }

    /// <summary>A name, or bracketed names separated by commas, added to <paramref name="names"/>.</summary>
    private bool Names(List<string> names, string what)
    {
        var bracketed = parser.Take(TokenKind.OpenParen);

        do
        {
            if (Current.Kind != TokenKind.Identifier)
            {
                parser.Complain(IssueCode.Syntax, $"expected a name for {what}, but found {parser.Found()}.");
                return false;
            }

            names.Add(Current.Text);
            parser.Advance();
        }
        while (bracketed && parser.Take(TokenKind.Comma));

        if (bracketed && !parser.Expect(TokenKind.CloseParen, $"')' to close the names for {what}")) return false;

        return true;
    }

    /// <summary>Whether an <c>if</c> starts here, rather than a binding or a socket somebody called that.</summary>
    private bool AtIf() =>
        Current.Text == "if"
        && Ahead().Kind is not (TokenKind.Assign or TokenKind.Colon or TokenKind.Dot or TokenKind.Pipe
            or TokenKind.Comma or TokenKind.CloseParen or TokenKind.NewLine or TokenKind.End);

    /// <summary><c>if condition { ... } else { ... }</c>, the <c>else</c> optional and allowed on the next line.</summary>
    private Expr? If(int line, int column)
    {
        parser.Advance();

        if (Condition() is not { } condition) return null;
        if (Branch("if") is not { } then) return null;

        var mark = 0;
        while (Ahead(mark).Kind == TokenKind.NewLine) mark++;

        if (Ahead(mark) is not { Kind: TokenKind.Identifier, Text: "else" }) return Built(new IfExpr(condition, then, null, line, column), condition);

        parser.Advance(mark + 1);

        Branch otherwise;

        if (Current is { Kind: TokenKind.Identifier, Text: "if" })
        {
            var nested = Current;

            if (If(nested.Line, nested.Column) is not { } next) return null;

            otherwise = new Branch([], next, null);
        }
        else if (Branch("else") is { } written)
        {
            otherwise = written;
        }
        else
        {
            return null;
        }

        return Built(new IfExpr(condition, then, otherwise, line, column), condition);
    }

    /// <summary>A branch's braces and what is in them.</summary>
    private Branch? Branch(string word)
    {
        if (!parser.Take(TokenKind.OpenBrace))
        {
            parser.Complain(IssueCode.Syntax, $"expected '{{' to open what '{word}' builds, but found {parser.Found()}.");
            return null;
        }

        return parser.Braced(out var body, out var result, out var results) ? new Branch(body, result, results) : null;
    }

    /// <summary>
    /// What an <c>if</c> decides on: comparisons joined by <c>and</c>, <c>or</c> and
    /// <c>not</c>, or a plain value, which holds where it is not zero.
    /// </summary>
    private Expr? Condition()
    {
        if (Both() is not { } left) return null;

        while (Current is { Kind: TokenKind.Identifier, Text: "or" })
        {
            var (line, column) = (Current.Line, Current.Column);

            parser.Advance();

            if (Both() is not { } right) return null;
            if (Built(new LogicExpr(false, left, right, line, column), left, right) is not { } joined) return null;

            left = joined;
        }

        return left;
    }

    private Expr? Both()
    {
        if (Negated() is not { } left) return null;

        while (Current is { Kind: TokenKind.Identifier, Text: "and" })
        {
            var (line, column) = (Current.Line, Current.Column);

            parser.Advance();

            if (Negated() is not { } right) return null;
            if (Built(new LogicExpr(true, left, right, line, column), left, right) is not { } joined) return null;

            left = joined;
        }

        return left;
    }

    private Expr? Negated()
    {
        if (Current is not { Kind: TokenKind.Identifier, Text: "not" } || !Starts(Ahead())) return Compared();

        var (line, column) = (Current.Line, Current.Column);

        parser.Advance();

        return Negated() is { } operand ? Built(new NotExpr(operand, line, column), operand) : null;
    }

    private Expr? Compared()
    {
        if (Pipeline() is not { } left) return null;
        if (!Comparing(Current.Kind)) return left;

        var op = Current.Kind;
        var (line, column) = (Current.Line, Current.Column);

        parser.Advance();

        if (Pipeline() is not { } right) return null;

        if (Comparing(Current.Kind))
            return Refuse(IssueCode.Syntax, $"'{Current.Text}' follows a comparison. Join two comparisons with 'and' or 'or'.");

        return Built(new CompareExpr(op, left, right, line, column), left, right);
    }

    internal static bool Comparing(TokenKind kind) => kind
        is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessOrEqual
        or TokenKind.Greater or TokenKind.GreaterOrEqual;

    /// <summary>Whether a value can start at <paramref name="token"/>, so a word before it is an operator rather than a name.</summary>
    private static bool Starts(Token token) => token.Kind
        is TokenKind.Number or TokenKind.Text or TokenKind.Identifier or TokenKind.OpenParen or TokenKind.Minus or TokenKind.Block;

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
            var named = parts.Count switch
            {
                1 => new NameExpr(parts[0], null, line, column),
                2 => new NameExpr(parts[0], parts[1], line, column),
                // Only a call can carry a full type id, since nothing reads an
                // output off one.
                _ => Refuse(IssueCode.Syntax, $"'{string.Join('.', parts)}' is not a name this can read."),
            };

            return named is null ? null : Indexed(named, After(Ahead(-1)));
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

        if (parser.Take(TokenKind.Spread))
        {
            if (name is not null)
            {
                parser.Complain(IssueCode.Syntax, $"'...' spreads a list over the sockets left free, so it goes without a name: '...list', not '{name}: ...list'.");
                return null;
            }

            return Pipeline() is { } spread ? new Argument(null, spread, line, column, Spread: true) : null;
        }

        return Pipeline() is { } value ? new Argument(name, value, line, column) : null;
    }

    private bool AtValue() => Current.Kind
        is TokenKind.Number or TokenKind.Text or TokenKind.Identifier or TokenKind.OpenParen or TokenKind.Minus or TokenKind.Block;

    private static string Closing(string closer, string opener, Token open) =>
        $"'{closer}' to close the '{opener}' on line {open.Line}, column {open.Column}";
}
